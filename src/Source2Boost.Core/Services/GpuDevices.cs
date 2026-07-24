using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Общий поиск дискретных дисплейных адаптеров (NVIDIA/AMD) в HKLM\...\Enum\PCI.
/// Используется твиками прерываний (MSI-режим, interrupt-affinity), чтобы не дублировать логику.
/// </summary>
public static class GpuDevices
{
    private const string PciRoot = @"SYSTEM\CurrentControlSet\Enum\PCI";
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private static readonly string[] Vendors = { "VEN_10DE", "VEN_1002" }; // NVIDIA, AMD

    /// <summary>Относительные пути инстансов дискретных GPU (NVIDIA/AMD) с классом дисплейного адаптера.
    /// Напр. SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&amp;...\4&amp;abc&amp;0&amp;0008.</summary>
    public static List<string> FindDiscreteDisplayAdapters()
    {
        var result = new List<string>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var pci = root.OpenSubKey(PciRoot);
            if (pci is null) return result;
            foreach (var vendor in pci.GetSubKeyNames())
            {
                if (!Vendors.Any(v => vendor.StartsWith(v, StringComparison.OrdinalIgnoreCase))) continue;
                using var vk = pci.OpenSubKey(vendor);
                if (vk is null) continue;
                foreach (var inst in vk.GetSubKeyNames())
                {
                    using var ik = vk.OpenSubKey(inst);
                    var cls = ik?.GetValue("ClassGUID") as string;
                    if (cls is null || !cls.Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add($@"{PciRoot}\{vendor}\{inst}");
                }
            }
        }
        catch { }
        return result;
    }
}
