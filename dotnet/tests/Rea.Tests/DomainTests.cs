using Rea.Domain;

namespace Rea.Tests;

internal static class DomainTests
{
    public static IEnumerable<Case> Cases()
    {
        foreach (var wide in new[] { false, true })
        foreach (var little in new[] { false, true })
        foreach (var (machine, architecture) in new (ushort, string)[] { (3, "x86"), (62, "x86_64"), (8, "mips"), (20, "powerpc"), (40, "arm"), (183, "arm64"), (243, "riscv") })
            yield return Case.Sync($"ELF {wide} {little} {architecture}", () =>
            {
                var bytes = Fixtures.Elf(wide, little, machine);
                Fixtures.U16(bytes, 16, 3, little); Fixtures.U32(bytes, wide ? 48 : 36, 0x81234567, little);
                var parsed = Check.Success(ExecutableHeader.Parse(bytes, "x64"));
                Check.Equal(architecture, parsed.Architecture);
                if (parsed is ElfFamilyExecutableMetadata family)
                {
                    var facts = family.Mips ?? family.Riscv ?? family.Powerpc!;
                    Check.Equal(wide ? 64 : 32, facts.ElfClass); Check.Equal(little ? "little" : "big", facts.ByteOrder);
                    Check.Equal((ushort)3, facts.Type); Check.Equal(0x81234567u, facts.Flags);
                }
            });

        foreach (var (machine, architecture, wide) in new (ushort, string, bool)[] { (0x014c, "x86", false), (0x8664, "x86_64", true), (0x01c0, "arm", false), (0x01c2, "arm", false), (0x01c4, "arm", false), (0xaa64, "arm64", true) })
        foreach (var (flags, role) in new (ushort, string)[] { (0, "non-executable"), (2, "application"), (0x2002, "shared-library") })
        foreach (var managed in new[] { false, true })
            yield return Case.Sync($"PE {machine:x4} {role} managed={managed}", () =>
            {
                var parsed = (PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(Fixtures.Pe(machine, managed, flags, wide), "x64"));
                Check.Equal(machine, parsed.PeMachine); Check.Equal(architecture, parsed.Architecture);
                Check.Equal(managed, parsed.Managed); Check.Equal(role, parsed.ExecutableRole);
            });

        foreach (var os in new ushort[] { 0x4644, 0xadc4, 0xadc5, 0x7b79, 0x1993, 0x1992 })
            yield return Case.Sync($"ReadyToRun OS {os:x4}", () =>
            {
                var machine = (ushort)(0x8664 ^ os);
                var parsed = (PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(Fixtures.Pe(machine, true), "x64"));
                Check.Equal("x86_64", parsed.Architecture); Check.Equal(machine, parsed.PeMachine);
                Check.Equal("unsupported PE architecture", Check.Failure(ExecutableHeader.Parse(Fixtures.Pe(machine), "x64")));
            });

        yield return Case.Sync("PE CLI directory preserves either nonzero declaration", () =>
        {
            foreach (var field in new[] { 0, 4 })
            {
                var bytes = Fixtures.Pe(managed: true); Fixtures.U32(bytes, 200 + 14 * 8 + field, 0);
                Check.True(((PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(bytes, "x64"))).Managed);
            }
        });
        yield return Case.Sync("PE optional header rejects inconsistent commitments", () =>
        {
            var bytes = Fixtures.Pe(); Fixtures.U16(bytes, 88, 0x10b);
            Check.Equal("PE optional-header magic does not match its architecture", Check.Failure(ExecutableHeader.Parse(bytes, "x64")));
            bytes = Fixtures.Pe(); Fixtures.U32(bytes, 196, uint.MaxValue);
            Check.Equal("truncated PE data-directory table", Check.Failure(ExecutableHeader.Parse(bytes, "x64")));
        });
        foreach (var rsds in new[] { true, false })
            yield return Case.Sync($"CodeView preserves source path rsds={rsds}", () =>
            {
                var data = Fixtures.CodeView(rsds); var parsed = PeHeader.ParseCodeViewData(data, (uint)data.Length)!;
                Check.Equal("C:\\caller\\private\\symbols.pdb", parsed.PdbPath); Check.Equal("symbols.pdb", parsed.PdbFileName);
                Check.Equal(7u, parsed.Age); Check.Equal(rsds ? "01234567-89ab-cdef-0123-456789abcdef" : null, parsed.Guid);
                Check.True(PeHeader.ParseCodeViewData(data.AsSpan(0, data.Length - 1), (uint)data.Length) is null);
            });
        yield return Case.Sync("PE debug directory locates physical and RVA records", () =>
        {
            var bytes = Fixtures.Pe(codeView: true);
            Check.Equal("symbols.pdb", ((PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(bytes, "x64"))).CodeView!.PdbFileName);
            Fixtures.U32(bytes, 536, 0);
            Check.Equal("symbols.pdb", ((PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(bytes, "x64"))).CodeView!.PdbFileName);
            Fixtures.U32(bytes, 532, uint.MaxValue);
            Check.True(((PeExecutableMetadata)Check.Success(ExecutableHeader.Parse(bytes, "x64"))).CodeView is null);
        });
        yield return Case.Sync("RVA mapping requires raw-file coverage and does not wrap", () =>
        {
            var sections = new byte[40]; Fixtures.U32(sections, 12, 0x1000); Fixtures.U32(sections, 16, 16); Fixtures.U32(sections, 20, uint.MaxValue);
            Check.Equal((long?)uint.MaxValue + 4, PeHeader.RvaToFileOffset(0x1004, sections, 1, 0));
            Check.True(PeHeader.RvaToFileOffset(0x100f, sections, 1, 0, 2) is null);
        });

        foreach (var wide in new[] { false, true })
        foreach (var little in new[] { false, true })
            yield return Case.Sync($"Mach-O and FAT wide={wide} little={little}", () =>
            {
                Check.Equal("mach-o", Check.Success(ExecutableHeader.Parse(Fixtures.ThinMach(wide, little), "x64")).Format);
                var bytes = Fixtures.Fat(wide, little);
                var arm = (UniversalMachOMetadata)Check.Success(ExecutableHeader.Parse(bytes, "arm64"));
                var intel = (UniversalMachOMetadata)Check.Success(ExecutableHeader.Parse(bytes, "x64"));
                Check.Equal("arm64", arm.Architecture); Check.Equal("x86_64", intel.Architecture);
                Check.Equal(2, arm.AvailableArchitectures.Count); Check.Equal(32L, arm.UniversalSlice.Size);
                Check.True(arm.UniversalSlice.Offset > intel.UniversalSlice.Offset);
                Check.Failure(ExecutableHeader.Parse(bytes, "arm"));
                // Every declaration must be valid, even when the host selects the other slice.
                if (wide) Fixtures.U64(bytes, 8 + 32 + 16, ulong.MaxValue, little);
                else Fixtures.U32(bytes, 8 + 20 + 12, uint.MaxValue, little);
                Check.Failure(ExecutableHeader.Parse(bytes, "x64"));
            });
        yield return Case.Sync("FAT range and alignment validation", () =>
        {
            var bytes = Fixtures.Fat(); Fixtures.U32(bytes, 8 + 20 + 16, 53, false);
            Check.True(Check.Failure(ExecutableHeader.Parse(bytes, "x64")).Contains("alignment exponent"));
            bytes = Fixtures.Fat(); Fixtures.U32(bytes, 8 + 20 + 8, 81, false);
            Check.True(Check.Failure(ExecutableHeader.Parse(bytes, "x64", 200)).Contains("not aligned"));
            bytes = Fixtures.Fat(); Fixtures.U32(bytes, 8 + 20 + 12, 0, false);
            Check.True(Check.Failure(ExecutableHeader.Parse(bytes, "x64")).Contains("invalid slice range"));
        });
        yield return Case.Sync("Mach-O prefix validates load commands against file size", () =>
        {
            var bytes = Fixtures.ThinMach(); Fixtures.U32(bytes, 20, 100);
            Check.Failure(ExecutableHeader.Parse(bytes, "x64"));
            Check.Success(ExecutableHeader.Parse(bytes, "x64", 132));
        });
        yield return Case.Sync("JVM magic is not sufficient for Mach-O identity", () =>
        {
            Check.Failure(ExecutableHeader.Parse(Fixtures.JvmClass(), "x64"));
            var unknown = Fixtures.Fat(); Fixtures.U32(unknown, 8, 123, false); Fixtures.U32(unknown, 28, 456, false);
            Check.Equal("FAT binary has no host-compatible x64 architecture", Check.Failure(ExecutableHeader.Parse(unknown, "x64")));
            Check.Equal("FAT slice identity is outside the bounded prefix", Check.Failure(ExecutableHeader.Parse(unknown.AsSpan(0, 48), "x64", unknown.Length)));
        });
        yield return Case.Sync("DOS MZ retains module and overlay coordinates", () =>
        {
            var bytes = Fixtures.DosMz(); var parsed = Check.Success(DosMz.Parse(bytes, 200));
            Check.Equal(32, parsed.HeaderBytes); Check.Equal(128L, parsed.ModuleBytes); Check.Equal(40L, parsed.OverlayBytes);
            Check.Equal("dos-mz", Check.Success(ExecutableHeader.Parse(bytes, "x64")).Format);
            Fixtures.U16(bytes, 20, 128); Check.Failure(DosMz.Parse(bytes, bytes.Length));
        });
        yield return Case.Sync("DOS MZ relocation owner validates destination", () =>
        {
            var bytes = Fixtures.DosMz(); Fixtures.U16(bytes, 6, 1); Fixtures.U16(bytes, 28, 127);
            Check.True(Check.Failure(DosMz.Parse(bytes, bytes.Length)).Contains("relocation 0"));
            Fixtures.U16(bytes, 28, 126); Check.Success(DosMz.Parse(bytes, bytes.Length));
        });
        yield return Case.Sync("DOS short header needs distant Windows signature", () =>
        {
            var bytes = Fixtures.DosMz(); Fixtures.U32(bytes, 60, 1000);
            Check.True(DosMz.ObserveWindowsHeader(bytes, 2000) is MzWindowsHeaderObservation.SignatureNeeded);
            Check.True(DosMz.ObserveWindowsHeader(bytes, 2000, "NE"u8) is MzWindowsHeaderObservation.Declared);
            Check.True(DosMz.ObserveWindowsHeader(bytes, 2000, "NO"u8) is MzWindowsHeaderObservation.Absent);
            Check.Failure(ExecutableHeader.Parse(bytes.AsSpan(0, 32), "x64", 160));
        });
        foreach (var length in new long[] { -1, 0, 1, 65280, 65281, long.MaxValue })
            yield return Case.Sync($"DOS COM length {length}", () => Check.Equal(length is >= 1 and <= 65280,
                ExecutableHeader.ParseDosCom(length) is Result<ExecutableMetadata, string>.Success));
        yield return Case.Sync("Every truncated header prefix and random mutation stays in Result", () =>
        {
            var corpus = new[] { Fixtures.Elf(), Fixtures.Pe(codeView: true), Fixtures.DosMz(), Fixtures.Fat(), Fixtures.Fat(true, true), Fixtures.ThinMach(), Fixtures.JvmClass() };
            foreach (var bytes in corpus)
                for (var length = 0; length <= bytes.Length; length++)
                    _ = ExecutableHeader.Parse(bytes.AsSpan(0, length), "x64", bytes.Length);
            var random = new Random(142);
            for (var i = 0; i < 10000; i++)
            {
                var bytes = (byte[])corpus[i % corpus.Length].Clone();
                for (var j = 0; j < 5; j++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                _ = ExecutableHeader.Parse(bytes, "x64");
            }
        });
    }
}
