// SPDX-License-Identifier: MPL-2.0
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// Based on LibreHardwareMonitor 76150732c2df9241415eec216001bec4557c9033.
// Modification: require a loaded PawnIO module and a successful, complete ioctl result.
// The upstream Execute() returns a zero array on failure; that is not a valid MSR measurement.
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.PawnIo;

public class IntelMsr
{
    private readonly long[] _inArray = new long[1];
    private readonly long[] _outArray = new long[1];
    private readonly PawnIo _pawnIO = PawnIo.LoadModuleFromResource(typeof(IntelMsr).Assembly, "LibreHardwareMonitor.Resources.PawnIo.IntelMSR.bin");

    public bool ReadMsr(uint index, out ulong value)
    {
        value = 0;
        if (!_pawnIO.IsLoaded) return false;
        _inArray[0] = index;
        try {
            if (_pawnIO.ExecuteHr("ioctl_read_msr", _inArray, 1, _outArray, 1, out uint length) != 0 || length != 1) return false;
            value = unchecked((ulong)_outArray[0]);
            return true;
        } catch { return false; }
    }

    public bool ReadMsr(uint index, out uint eax, out uint edx)
    {
        bool valid = ReadMsr(index, out ulong value);
        eax = (uint)value; edx = (uint)(value >> 32);
        return valid;
    }

    public bool ReadMsr(uint index, out uint eax, out uint edx, GroupAffinity affinity)
    {
        GroupAffinity previous = ThreadAffinity.Set(affinity);
        try { return ReadMsr(index, out eax, out edx); }
        finally { ThreadAffinity.Set(previous); }
    }

    public void Close() => _pawnIO.Close();
}
