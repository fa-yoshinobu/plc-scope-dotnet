namespace PlcScope.Core.Models;

/// <summary>
/// Canonical SLMP request-destination module I/O targets.
/// The 13 names follow the canonical module I/O vocabulary shared across the
/// plc-comm family. JSON carries the member name (e.g. "OwnStation"); the wire
/// value is resolved from the PlcComm.Slmp <c>SlmpModuleIo</c> constants in the
/// SLMP session layer.
/// </summary>
public enum SlmpModuleIoTarget
{
    OwnStation,
    ControlSystemCpu,
    StandbySystemCpu,
    SystemACpu,
    SystemBCpu,
    MultipleCpu1,
    MultipleCpu2,
    MultipleCpu3,
    MultipleCpu4,
    RemoteHead1,
    RemoteHead2,
    ControlSystemRemoteHead,
    StandbySystemRemoteHead,
}
