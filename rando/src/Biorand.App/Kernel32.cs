// Ported from BioRand (biorand/classic, biorand/Kernel32.cs), MIT, Copyright (c) 2022-2026 Ted John.
// Biorand++: changes marked "Biorand++".

using System.Runtime.InteropServices;

namespace IntelOrca.Biohazard.BioRand
{
    internal static class Kernel32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFile(string lpFileName);
    }
}
