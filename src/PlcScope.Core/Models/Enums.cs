namespace PlcScope.Core.Models;

public enum ProtocolKind
{
    Slmp,
    HostLink,
    Toyopuc,
}

public enum TransportMode
{
    Tcp,
    Udp,
}

public enum KeyenceDeviceMode
{
    Normal,
    Xym,
}

public enum DeviceAddressDisplayRule
{
    Default,
    DecimalNoPadding,
    OctalNoPadding,
    KeyenceBitBank,
    KeyenceXymBit,
}

public enum DeviceKind
{
    Word,
    Bit,
}

public enum BlockDisplayMode
{
    Word,
    DWord,
    Float32,
    BitExpand,
}

public enum BitDisplayMode
{
    Packed16,
    SingleLine,
}

public enum DisplayRadix
{
    Dec,
    Hex,
}

public enum ValueDataType
{
    Bit,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float32,
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

public enum CpuRunState
{
    Unknown,
    Run,
    Stop,
    Program,
}

public enum CpuCommand
{
    Run,
    Stop,
}

public enum TraceDirection
{
    Send,
    Receive,
}

public enum MonitorRowKind
{
    Word,
    PackedBits,
    SingleBit,
    DWord,
    Float,
    ExpandedWordHeader,
    ExpandedBit,
}
