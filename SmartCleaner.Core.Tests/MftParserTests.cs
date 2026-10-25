using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 7 (ROADMAP v3.1, фаза M): контракты pure-функций парсера MFT-записей
/// на синтетических фикстурах — без доступа к тому. Поток как в Дне 8:
/// read → ApplyFixups → ParseRecord.
/// </summary>
public class MftParserTests
{
    private static MftRecord BuildFixupAndParse(MftRecordBuilder builder)
    {
        byte[] record = builder.Build();
        Assert.Null(MftParser.ApplyFixups(record, builder.BytesPerSector));
        return MftParser.ParseRecord(record, builder.RecordSize);
    }

    // ---------- ApplyFixups: Update Sequence Array ----------

    [Fact]
    public void ApplyFixups_1024Record512Sectors_RestoresOriginalTails()
    {
        var record = new byte[1024];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30); // смещение USA
        MftTestBytes.PutU16(record, 0x06, 3);    // 2 сектора + тег
        MftTestBytes.PutU16(record, 0x30, 0x1234); // usa[0] — тег
        MftTestBytes.PutU16(record, 0x32, 0xABCD); // usa[1] — оригинал хвоста сектора 0
        MftTestBytes.PutU16(record, 0x34, 0xEF01); // usa[2] — оригинал хвоста сектора 1
        // Дисковая форма: хвосты секторов (0x1FE/0x3FE) затёрты тегом
        MftTestBytes.PutU16(record, 0x1FE, 0x1234);
        MftTestBytes.PutU16(record, 0x3FE, 0x1234);

        MftRecordError? error = MftParser.ApplyFixups(record, 512);

        Assert.Null(error);
        Assert.Equal((ushort)0xABCD, MftTestBytes.GetU16(record, 0x1FE));
        Assert.Equal((ushort)0xEF01, MftTestBytes.GetU16(record, 0x3FE));
    }

    [Fact]
    public void ApplyFixups_TornSectorTail_ReturnsTornRead()
    {
        var record = new byte[1024];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30);
        MftTestBytes.PutU16(record, 0x06, 3);
        MftTestBytes.PutU16(record, 0x30, 0x1234);
        MftTestBytes.PutU16(record, 0x32, 0xABCD);
        MftTestBytes.PutU16(record, 0x34, 0xEF01);
        MftTestBytes.PutU16(record, 0x1FE, 0x1234);
        MftTestBytes.PutU16(record, 0x3FE, 0xBEEF); // хвост второго сектора не равен тегу — torn read

        MftRecordError? error = MftParser.ApplyFixups(record, 512);

        Assert.Equal(MftRecordError.TornRead, error);
    }

    [Fact]
    public void ApplyFixups_512Record512Sectors_SingleFixupPoint()
    {
        var record = new byte[512];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30);
        MftTestBytes.PutU16(record, 0x06, 2); // 1 сектор + тег
        MftTestBytes.PutU16(record, 0x30, 0x4D2);
        MftTestBytes.PutU16(record, 0x32, 0x55AA);
        MftTestBytes.PutU16(record, 0x1FE, 0x4D2);

        MftRecordError? error = MftParser.ApplyFixups(record, 512);

        Assert.Null(error);
        Assert.Equal((ushort)0x55AA, MftTestBytes.GetU16(record, 0x1FE));
    }

    [Fact]
    public void ApplyFixups_4096Record4096Sectors_SingleFixupPoint()
    {
        var record = new byte[4096];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30);
        MftTestBytes.PutU16(record, 0x06, 2); // 1 сектор 4096 + тег — хвост на 0xFFE
        MftTestBytes.PutU16(record, 0x30, 0x7788);
        MftTestBytes.PutU16(record, 0x32, 0x0FF0);
        MftTestBytes.PutU16(record, 0xFFE, 0x7788);

        MftRecordError? error = MftParser.ApplyFixups(record, 4096);

        Assert.Null(error);
        Assert.Equal((ushort)0x0FF0, MftTestBytes.GetU16(record, 0xFFE));
    }

    [Fact]
    public void ApplyFixups_StrideInferred_WhenRecordNotSectorMultiple()
    {
        // 4Kn-том: сектор 4096, а запись 1024 — stride выводится из USA-каунта
        var record = new byte[1024];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30);
        MftTestBytes.PutU16(record, 0x06, 3);  // 2 фиксап-слота → stride 512
        MftTestBytes.PutU16(record, 0x30, 0x1234);
        MftTestBytes.PutU16(record, 0x32, 0x1111);
        MftTestBytes.PutU16(record, 0x34, 0x2222);
        MftTestBytes.PutU16(record, 0x1FE, 0x1234);
        MftTestBytes.PutU16(record, 0x3FE, 0x1234);

        MftRecordError? error = MftParser.ApplyFixups(record, 4096);

        Assert.Null(error);
        Assert.Equal((ushort)0x1111, MftTestBytes.GetU16(record, 0x1FE));
        Assert.Equal((ushort)0x2222, MftTestBytes.GetU16(record, 0x3FE));
    }

    [Fact]
    public void ApplyFixups_MalformedUsa_CountMismatchAndOffsetOutOfBounds_ReturnsMalformed()
    {
        // (a) каунт USA не согласован с числом секторов записи
        var countMismatch = new byte[1024];
        MftTestBytes.WriteMagic(countMismatch, "FILE");
        MftTestBytes.PutU16(countMismatch, 0x04, 0x30);
        MftTestBytes.PutU16(countMismatch, 0x06, 5); // ожидалось 3

        Assert.Equal(MftRecordError.Malformed, MftParser.ApplyFixups(countMismatch, 512));

        // (b) смещение USA за пределами записи
        var offsetOob = new byte[1024];
        MftTestBytes.WriteMagic(offsetOob, "FILE");
        MftTestBytes.PutU16(offsetOob, 0x04, 0xEFF0);
        MftTestBytes.PutU16(offsetOob, 0x06, 3);

        Assert.Equal(MftRecordError.Malformed, MftParser.ApplyFixups(offsetOob, 512));
    }

    [Fact]
    public void ApplyFixups_NonFileMagic_ReturnsNotARecordOrCorrupt()
    {
        var zeroed = new byte[1024];
        Assert.Equal(MftRecordError.NotARecord, MftParser.ApplyFixups(zeroed, 512));

        var baad = new byte[1024];
        MftTestBytes.WriteMagic(baad, "BAAD");
        Assert.Equal(MftRecordError.Corrupt, MftParser.ApplyFixups(baad, 512));
    }

    // ---------- ParseRecord: синтетические записи ----------

    [Fact]
    public void ParseRecord_RegularFile_ResidentData()
    {
        var builder = new MftRecordBuilder(recordNumber: 42, sequence: 7);
        builder.AddFileName("notes.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        builder.AddResidentData(new byte[] { 1, 2, 3, 4, 5 });

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal(42UL, record.Number);
        Assert.Null(record.BaseRecordNumber);
        Assert.Equal("notes.txt", record.BestFileName);
        Assert.Equal((ushort)7, record.Sequence);
        Assert.Equal(5UL, record.ParentRecordNumber);
        Assert.Equal((ushort)5, record.ParentSequence);
        Assert.Equal(0UL, record.AllocatedSize); // резидентный $DATA не занимает кластеров
        Assert.Equal(5UL, record.RealSize);
        Assert.Equal(MftRecordFlags.InUse, record.Flags);
        Assert.Equal(0, record.ExtraLinks);
        Assert.False(record.HasAttributeList);
    }

    [Fact]
    public void ParseRecord_Directory_NoDataSizes()
    {
        var builder = new MftRecordBuilder { Flags = MftRecordFlags.InUse | MftRecordFlags.Directory };
        builder.AddFileName("Program Files", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal(MftRecordFlags.InUse | MftRecordFlags.Directory, record.Flags);
        Assert.Equal("Program Files", record.BestFileName);
        Assert.Equal(0UL, record.AllocatedSize); // каталог не имеет $DATA
        Assert.Equal(0UL, record.RealSize);
    }

    [Fact]
    public void ParseRecord_NonResidentData_SizesFromHeader()
    {
        var builder = new MftRecordBuilder(recordNumber: 100);
        builder.AddFileName("big.bin", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        builder.AddNonResidentData(allocatedSize: 8192, realSize: 6000);

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal(8192UL, record.AllocatedSize);
        Assert.Equal(6000UL, record.RealSize);
        Assert.Equal("big.bin", record.BestFileName);
    }

    [Fact]
    public void ParseRecord_Hardlink_TwoFileNames_FirstNonDosWinsAndExtraLinks()
    {
        var builder = new MftRecordBuilder { LinkCount = 2 };
        builder.AddFileName("hard.txt", MftNamespace.Win32, parentNumber: 100, parentSequence: 3);
        builder.AddFileName("alias.txt", MftNamespace.Win32, parentNumber: 200, parentSequence: 8);
        builder.AddResidentData(new byte[] { 9, 9 });

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal("hard.txt", record.BestFileName);
        Assert.Equal(100UL, record.ParentRecordNumber); // родитель первого не-DOS имени
        Assert.Equal((ushort)3, record.ParentSequence);
        Assert.Equal(1, record.ExtraLinks); // linkCount 2 − 1 каноническая связь
    }

    [Fact]
    public void ParseRecord_ExtensionRecord_BaseRecordNumber()
    {
        var builder = new MftRecordBuilder { BaseRecordNumber = 1234 };

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal(1234UL, record.BaseRecordNumber);
        Assert.Null(record.BestFileName); // extension-запись не содержит имён
        Assert.Null(record.ParentRecordNumber);
    }

    [Fact]
    public void ParseRecord_Deleted_NotInUse()
    {
        var builder = new MftRecordBuilder { Flags = MftRecordFlags.None };
        builder.AddFileName("ghost.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        builder.AddResidentData(new byte[10]);

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Equal(MftRecordError.Deleted, record.Error);
        Assert.Equal("ghost.txt", record.BestFileName); // содержимое всё равно разобрано
        Assert.Equal(10UL, record.RealSize);
    }

    [Fact]
    public void ParseRecord_MagicClassification_ZeroBaadGarbage()
    {
        Assert.Equal(MftRecordError.NotARecord, MftParser.ParseRecord(new byte[1024], 1024).Error);

        var baad = new byte[1024];
        MftTestBytes.WriteMagic(baad, "BAAD");
        Assert.Equal(MftRecordError.Corrupt, MftParser.ParseRecord(baad, 1024).Error);

        var garbage = new byte[1024];
        MftTestBytes.WriteMagic(garbage, "GARB");
        Assert.Equal(MftRecordError.NotARecord, MftParser.ParseRecord(garbage, 1024).Error);
    }

    [Fact]
    public void ParseRecord_TornRead_FromBuilderFixture()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("torn.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        byte[] record = builder.Build();
        record[0x1FE] ^= 0xFF; // порча хвоста первого сектора

        Assert.Equal(MftRecordError.TornRead, MftParser.ApplyFixups(record, 512));
    }

    [Fact]
    public void ParseRecord_Malformed_UsaOffsetOutOfBounds()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("x.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        byte[] record = builder.Build();
        Assert.Null(MftParser.ApplyFixups(record, 512));
        MftTestBytes.PutU16(record, 0x04, 0xEFF0); // USA за пределами — после фиксапа портим заголовок

        Assert.Equal(MftRecordError.Malformed, MftParser.ParseRecord(record, 1024).Error);
    }

    [Fact]
    public void ParseRecord_DosOnlyName_BestFileNameNull_ParentKept()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("NOTES~1.TXT", MftNamespace.Dos, parentNumber: 5, parentSequence: 5);
        builder.AddResidentData(new byte[] { 1 });

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Null(record.BestFileName); // DOS 8.3 отбрасывается
        Assert.Equal(5UL, record.ParentRecordNumber); // родитель берётся и из DOS-имени
        Assert.Equal((ushort)5, record.ParentSequence);
        Assert.Equal(1UL, record.RealSize);
    }

    [Fact]
    public void ParseRecord_PosixName_Accepted()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("posix_name", MftNamespace.Posix, parentNumber: 12, parentSequence: 4);

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.Equal("posix_name", record.BestFileName);
        Assert.Equal(12UL, record.ParentRecordNumber);
    }

    [Fact]
    public void ParseRecord_AttributeLengthSpanningSectorTail_RequiresFixup()
    {
        // Атрибут $FILE_NAME размещён так, что поле length (0x1FC..0x1FF)
        // пересекает хвост сектора 0x1FE: без ApplyFixups запись неотличима от битой.
        var builder = new MftRecordBuilder();
        builder.AddResidentData(new byte[424]); // attr1: [0x38, 0x1F8)
        builder.AddFileName("notes.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        byte[] record = builder.Build();

        // Сырая (не зафиксапленная) запись — Malformed
        Assert.Equal(MftRecordError.Malformed, MftParser.ParseRecord(record, 1024).Error);

        // После фиксапа — корректный разбор
        Assert.Null(MftParser.ApplyFixups(record, 512));
        MftRecord parsed = MftParser.ParseRecord(record, 1024);
        Assert.Null(parsed.Error);
        Assert.Equal("notes.txt", parsed.BestFileName);
        Assert.Equal(424UL, parsed.RealSize);
    }

    [Fact]
    public void ParseRecord_AttributeList_Detected()
    {
        var builder = new MftRecordBuilder();
        builder.AddAttributeList();
        builder.AddFileName("data.bin", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);

        MftRecord record = BuildFixupAndParse(builder);

        Assert.Null(record.Error);
        Assert.True(record.HasAttributeList);
    }

    // ---------- Кейс-эджи: харднинг контрактов ----------

    [Fact]
    public void ParseRecord_NamedDataStream_Ignored()
    {
        // Только именованный поток (ADS) — содержимым файла не является
        var namedOnly = new MftRecordBuilder();
        namedOnly.AddFileName("ads.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        namedOnly.AddNamedData("zone", new byte[] { 1, 2, 3 });

        MftRecord namedOnlyRecord = BuildFixupAndParse(namedOnly);

        Assert.Null(namedOnlyRecord.Error);
        Assert.Equal(0UL, namedOnlyRecord.AllocatedSize);
        Assert.Equal(0UL, namedOnlyRecord.RealSize);

        // Именованный + неименованный: размеры только из неименованного
        var withUnnamed = new MftRecordBuilder();
        withUnnamed.AddFileName("ads2.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        withUnnamed.AddNamedData("zone", new byte[] { 1, 2, 3 });
        withUnnamed.AddResidentData(new byte[] { 7, 7, 7, 7 });

        MftRecord withUnnamedRecord = BuildFixupAndParse(withUnnamed);

        Assert.Null(withUnnamedRecord.Error);
        Assert.Equal(0UL, withUnnamedRecord.AllocatedSize);
        Assert.Equal(4UL, withUnnamedRecord.RealSize);
    }

    [Fact]
    public void ParseRecord_Malformed_AttributeLengthBeyondRecord()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("len.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        byte[] record = builder.Build();
        Assert.Null(MftParser.ApplyFixups(record, 512));
        MftTestBytes.PutU32(record, 0x38 + 0x04, 0x800); // длина первого атрибута вылазит за запись

        Assert.Equal(MftRecordError.Malformed, MftParser.ParseRecord(record, 1024).Error);
    }

    [Fact]
    public void ParseRecord_Malformed_TruncatedSpan()
    {
        var builder = new MftRecordBuilder();
        builder.AddFileName("cut.txt", MftNamespace.Win32AndDos, parentNumber: 5, parentSequence: 5);
        byte[] record = builder.Build();
        ReadOnlySpan<byte> truncated = record.AsSpan(0, 512); // половина записи

        Assert.Equal(MftRecordError.Malformed, MftParser.ParseRecord(truncated, 1024).Error);
    }

    [Fact]
    public void ApplyFixups_Malformed_UsaCountTooSmall()
    {
        var record = new byte[1024];
        MftTestBytes.WriteMagic(record, "FILE");
        MftTestBytes.PutU16(record, 0x04, 0x30);
        MftTestBytes.PutU16(record, 0x06, 1); // USA без единого слота данных

        Assert.Equal(MftRecordError.Malformed, MftParser.ApplyFixups(record, 512));
    }
}
