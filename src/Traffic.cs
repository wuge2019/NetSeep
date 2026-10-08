// NetSeep - 网卡枚举与流量采样（直接按偏移读取 MIB_IF_ROW2，免结构体、免第三方库）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetSeep
{
    /// <summary>一块网卡的简要信息。</summary>
    internal sealed class NicEntry
    {
        public Guid Id = Guid.Empty;
        public string Alias = "";
        public string Description = "";
        public uint Type;
        public bool IsUp;
        /// <summary>NDIS 过滤层（WFP / QoS / 安全软件过滤驱动）。计数与物理网卡相同，必须排除。</summary>
        public bool IsFilter;
        /// <summary>带 MAC 地址的真实适配器（WAN Miniport、Wi-Fi Direct 之类没有）。</summary>
        public bool IsPhysical;
        public bool IsTunnel;

        /// <summary>菜单里显示的名字：优先用系统友好名称（“以太网”“WLAN”）。</summary>
        public string Display
        {
            get
            {
                string name = string.IsNullOrEmpty(Alias) ? Description : Alias;
                if (string.IsNullOrEmpty(name)) name = "未知网卡";
                return name;
            }
        }

        public string ToolTip
        {
            get { return string.IsNullOrEmpty(Description) ? Display : Description; }
        }
    }

    /// <summary>一次采样的结果。</summary>
    internal struct TrafficSample
    {
        public double UpBps;
        public double DownBps;
        public long UpBytes;
        public long DownBytes;
    }

    /// <summary>
    /// MIB_IF_ROW2 的行布局。系统/SDK 不同时，行长度可能是 1352（PermanentPhysicalAddress
    /// 为 UCHAR[32]）或 1448（为 ULONG[32]），其后的字段偏移整体平移 96 字节。
    /// 启动时用 NET_LUID 位域等特征自动挑选正确的一份。
    /// </summary>
    internal sealed class IfRowLayout
    {
        public string Name;
        public int Stride;
        public int MtuOffset;
        public int TypeOffset;
        public int FlagsOffset;
        public int OperStatusOffset;
        public int PhysicalAddressLengthOffset;
        public int InOctetsOffset;
        public int OutOctetsOffset;

        // 两种布局里都相同的偏移
        public const int LuidOffset = 0;
        public const int IndexOffset = 8;
        public const int GuidOffset = 12;
        public const int AliasOffset = 28;
        public const int DescriptionOffset = 542;

        /// <summary>InterfaceAndOperStatusFlags 的 bit1：1 表示这是 NDIS 过滤层而非真实网卡。</summary>
        public const byte FilterInterfaceBit = 0x02;

        public static readonly IfRowLayout Compact = new IfRowLayout
        {
            Name = "1352 (UCHAR[32])",
            Stride = 1352,
            PhysicalAddressLengthOffset = 1056,
            MtuOffset = 1124,
            TypeOffset = 1128,
            FlagsOffset = 1152,
            OperStatusOffset = 1156,
            InOctetsOffset = 1208,
            OutOctetsOffset = 1216
        };

        public static readonly IfRowLayout Wide = new IfRowLayout
        {
            Name = "1448 (ULONG[32])",
            Stride = 1448,
            PhysicalAddressLengthOffset = 1056,
            MtuOffset = 1220,
            TypeOffset = 1224,
            FlagsOffset = 1248,
            OperStatusOffset = 1252,
            InOctetsOffset = 1304,
            OutOctetsOffset = 1312
        };

        public static readonly IfRowLayout[] Candidates = new IfRowLayout[] { Compact, Wide };

        /// <summary>NET_LUID 位域：低 24 位保留为 0，中间 24 位为索引，高 16 位为网卡类型。</summary>
        public static bool ValidLuid(ulong v)
        {
            if (v == 0) return false;
            if ((v & 0xFFFFFFUL) != 0) return false;
            ulong index = (v >> 24) & 0xFFFFFFUL;
            ulong type = (v >> 48) & 0xFFFFUL;
            return index > 0 && index < 100000 && type >= 1 && type <= 244;
        }

        /// <summary>用一行数据的各项特征判断该布局是否自洽。</summary>
        public bool LooksSane(IntPtr row)
        {
            if (!ValidLuid((ulong)Marshal.ReadInt64(row, LuidOffset))) return false;

            uint type = (uint)Marshal.ReadInt32(row, TypeOffset);
            if (type < 1 || type > 244) return false;

            uint oper = (uint)Marshal.ReadInt32(row, OperStatusOffset);
            if (oper > 5) return false;

            uint paLen = (uint)Marshal.ReadInt32(row, PhysicalAddressLengthOffset);
            if (paLen > 32) return false;

            uint mtu = (uint)Marshal.ReadInt32(row, MtuOffset);
            if (mtu != 0 && (mtu < 68 || mtu > 65535)) return false;

            return true;
        }
    }

    internal static class NicTable
    {
        private static IfRowLayout _layout;

        /// <summary>探测结果描述，供 --dump / 日志显示。</summary>
        public static string LayoutDescription = "未探测";

        /// <summary>当前生效的行布局（首次访问时自动探测）。</summary>
        public static IfRowLayout Layout
        {
            get
            {
                if (_layout == null) _layout = DetectLayout();
                return _layout;
            }
        }

        internal static IntPtr RowPtr(IntPtr table, int index, IfRowLayout layout)
        {
            return new IntPtr(table.ToInt64() + Native.IF_TABLE_ROWS_OFFSET + (long)index * layout.Stride);
        }

        private static IfRowLayout DetectLayout()
        {
            IntPtr table;
            if (Native.GetIfTable2(out table) != 0 || table == IntPtr.Zero)
            {
                LayoutDescription = "GetIfTable2 失败，回退 1352";
                return IfRowLayout.Compact;
            }

            try
            {
                int count = Marshal.ReadInt32(table, 0);

                // 只探测前若干行，保证即使候选步长偏大也不会读出表外
                int probe = count;
                if (probe > 32) probe = 32;
                int safe = (int)((long)count * IfRowLayout.Compact.Stride / IfRowLayout.Wide.Stride);
                if (probe > safe) probe = safe;
                if (probe < 0) probe = 0;

                IfRowLayout best = IfRowLayout.Compact;
                int bestScore = -1;
                string detail = "";
                foreach (IfRowLayout c in IfRowLayout.Candidates)
                {
                    int score = 0;
                    for (int i = 0; i < probe; i++)
                        if (c.LooksSane(RowPtr(table, i, c))) score++;
                    detail += string.Format(" {0}={1}/{2};", c.Name, score, probe);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                LayoutDescription = string.Format("{0}（命中 {1}/{2}；候选{3}）", best.Name, bestScore, probe, detail);
                return best;
            }
            finally
            {
                Native.FreeMibTable(table);
            }
        }

        /// <summary>读取一行里的关键信息。</summary>
        internal static NicEntry ReadRow(IntPtr p, IfRowLayout layout)
        {
            NicEntry e = new NicEntry();
            e.Id = (Guid)Marshal.PtrToStructure(new IntPtr(p.ToInt64() + IfRowLayout.GuidOffset), typeof(Guid));
            e.Alias = ReadString(p, IfRowLayout.AliasOffset);
            e.Description = ReadString(p, IfRowLayout.DescriptionOffset);
            e.Type = (uint)Marshal.ReadInt32(p, layout.TypeOffset);
            e.IsUp = (uint)Marshal.ReadInt32(p, layout.OperStatusOffset) == Native.IF_OPER_STATUS_UP;
            e.IsFilter = (Marshal.ReadByte(p, layout.FlagsOffset) & IfRowLayout.FilterInterfaceBit) != 0;
            e.IsPhysical = (uint)Marshal.ReadInt32(p, layout.PhysicalAddressLengthOffset) > 0;
            e.IsTunnel = e.Type == (uint)Native.IF_TYPE_TUNNEL;
            return e;
        }

        /// <summary>列举真实网卡（排除回环与 NDIS 过滤层）。</summary>
        public static List<NicEntry> List()
        {
            List<NicEntry> list = new List<NicEntry>();
            IntPtr table;
            if (Native.GetIfTable2(out table) != 0 || table == IntPtr.Zero) return list;

            IfRowLayout layout = Layout;
            try
            {
                int count = Marshal.ReadInt32(table, 0);
                for (int i = 0; i < count; i++)
                {
                    NicEntry e = ReadRow(RowPtr(table, i, layout), layout);
                    if (e.IsFilter) continue;
                    if (e.Type == (uint)Native.IF_TYPE_SOFTWARE_LOOPBACK) continue;

                    bool dup = false;
                    foreach (NicEntry exist in list)
                    {
                        if (exist.Id == e.Id) { dup = true; break; }
                    }
                    if (dup) continue;

                    list.Add(e);
                }
            }
            finally
            {
                Native.FreeMibTable(table);
            }

            // 已连接的排前面，其余按名称排序，保证菜单顺序稳定
            list.Sort(delegate (NicEntry a, NicEntry b)
            {
                if (a.IsUp != b.IsUp) return a.IsUp ? -1 : 1;
                return string.Compare(a.Display, b.Display, StringComparison.CurrentCulture);
            });
            return list;
        }

        internal static string ReadString(IntPtr row, int offset)
        {
            string v = Marshal.PtrToStringUni(new IntPtr(row.ToInt64() + offset));
            return v == null ? "" : v;
        }
    }

    /// <summary>基于 GetIfTable2 的 64 位计数器做差分测速。</summary>
    internal sealed class TrafficSampler
    {
        private struct RowDiff
        {
            public Guid Id;
            public ulong Recv;
            public ulong Send;
            public bool Physical;
            public bool Eligible;   // 是否可以计入“合计”
        }

        private readonly Dictionary<Guid, ulong[]> _prev = new Dictionary<Guid, ulong[]>();
        private readonly Stopwatch _clock = new Stopwatch();

        public TrafficSampler()
        {
            _clock.Start();
        }

        /// <summary>只记录当前计数、不产生速率，避免启动瞬间出现巨大毛刺。</summary>
        public void Prime()
        {
            Sample(Guid.Empty);
        }

        /// <summary>
        /// 采样。<paramref name="nicFilter"/> 为空表示“合计”：只统计真实网卡
        /// （排除回环、隧道、NDIS 过滤层）；若存在带 MAC 的适配器，再排除
        /// WAN Miniport / Wi-Fi Direct 这类虚拟适配器，避免同一份流量算两次。
        /// </summary>
        public TrafficSample Sample(Guid nicFilter)
        {
            double dt = _clock.Elapsed.TotalSeconds;
            _clock.Restart();
            if (dt <= 0.001) dt = 1.0;

            TrafficSample result = new TrafficSample();
            IntPtr table;
            if (Native.GetIfTable2(out table) != 0 || table == IntPtr.Zero) return result;

            IfRowLayout layout = NicTable.Layout;
            ulong recvTotal = 0, sendTotal = 0;

            try
            {
                int count = Marshal.ReadInt32(table, 0);
                List<RowDiff> rows = new List<RowDiff>(count);
                bool anyPhysical = false;

                for (int i = 0; i < count; i++)
                {
                    IntPtr p = NicTable.RowPtr(table, i, layout);
                    Guid id = (Guid)Marshal.PtrToStructure(new IntPtr(p.ToInt64() + IfRowLayout.GuidOffset), typeof(Guid));
                    ulong recvNow = (ulong)Marshal.ReadInt64(p, layout.InOctetsOffset);
                    ulong sendNow = (ulong)Marshal.ReadInt64(p, layout.OutOctetsOffset);

                    // 每一行都要更新基准值（即使不统计），切换网卡时才不会出现假尖峰
                    ulong dRecv, dSend;
                    Diff(id, recvNow, sendNow, out dRecv, out dSend);

                    RowDiff rd = new RowDiff();
                    rd.Id = id;
                    rd.Recv = dRecv;
                    rd.Send = dSend;

                    if (nicFilter != Guid.Empty)
                    {
                        rd.Eligible = id == nicFilter;
                        rd.Physical = true;
                    }
                    else
                    {
                        NicEntry e = NicTable.ReadRow(p, layout);
                        rd.Physical = e.IsPhysical;
                        rd.Eligible = !e.IsFilter
                                      && !e.IsTunnel
                                      && e.IsUp
                                      && e.Type != (uint)Native.IF_TYPE_SOFTWARE_LOOPBACK;
                        if (rd.Eligible && e.IsPhysical) anyPhysical = true;
                    }

                    rows.Add(rd);
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    RowDiff rd = rows[i];
                    if (!rd.Eligible) continue;
                    if (nicFilter == Guid.Empty && anyPhysical && !rd.Physical) continue;
                    recvTotal += rd.Recv;
                    sendTotal += rd.Send;
                }
            }
            finally
            {
                Native.FreeMibTable(table);
            }

            // InOctets = 本机接收 = 下载；OutOctets = 本机发送 = 上传
            result.DownBytes = Clamp(recvTotal);
            result.UpBytes = Clamp(sendTotal);
            result.DownBps = result.DownBytes / dt;
            result.UpBps = result.UpBytes / dt;
            return result;
        }

        private static long Clamp(ulong v)
        {
            return v > long.MaxValue ? long.MaxValue : (long)v;
        }

        private void Diff(Guid id, ulong recvNow, ulong sendNow, out ulong dRecv, out ulong dSend)
        {
            ulong[] last;
            if (_prev.TryGetValue(id, out last))
            {
                // 计数器归零（网卡重置）时按 0 处理，避免出现天文数字
                dRecv = recvNow >= last[0] ? recvNow - last[0] : 0;
                dSend = sendNow >= last[1] ? sendNow - last[1] : 0;
                last[0] = recvNow;
                last[1] = sendNow;
            }
            else
            {
                _prev[id] = new ulong[] { recvNow, sendNow };
                dRecv = 0;
                dSend = 0;
            }
        }
    }

    /// <summary>速率与流量的人性化格式化。</summary>
    internal static class SpeedFormat
    {
        private const double KB = 1024.0;
        private const double MB = 1024.0 * 1024.0;
        private const double GB = 1024.0 * 1024.0 * 1024.0;

        /// <summary>把字节/秒拆成“数值 + 单位”，方便分开着色。</summary>
        public static void Split(double bps, out string value, out string unit)
        {
            if (double.IsNaN(bps) || bps < 0) bps = 0;

            if (bps >= GB)
            {
                double n = bps / GB;
                value = n.ToString(n >= 10 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
                unit = "GB/s";
            }
            else if (bps >= MB)
            {
                double n = bps / MB;
                string f = n >= 100 ? "0" : (n >= 10 ? "0.0" : "0.00");
                value = n.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
                unit = "MB/s";
            }
            else if (bps >= KB)
            {
                double n = bps / KB;
                value = n.ToString(n >= 100 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
                unit = "KB/s";
            }
            else if (bps >= 1)
            {
                value = Math.Round(bps).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                unit = "B/s";
            }
            else
            {
                value = "0";
                unit = "KB/s";
            }
        }

        /// <summary>累计流量（字节）。</summary>
        public static string Volume(double bytes)
        {
            if (bytes >= GB) return (bytes / GB).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            if (bytes >= MB) return (bytes / MB).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            if (bytes >= KB) return (bytes / KB).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            return Math.Round(bytes).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " B";
        }
    }
}
