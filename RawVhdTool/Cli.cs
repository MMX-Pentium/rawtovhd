namespace RawVhdTool;

internal static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }
            if (args.Length != 2)
                throw new CommandLineException("-a または -r と、対象ファイルを指定してください。");

            string path = Path.GetFullPath(args[1]);
            if (!File.Exists(path))
                throw new CommandLineException($"ファイルが見つかりません: {path}");

            switch (args[0])
            {
                case "-a":
                    AddFooter(path);
                    break;
                case "-r":
                    RemoveFooter(path);
                    break;
                default:
                    throw new CommandLineException($"不明なオプションです: {args[0]}");
            }

            return 0;
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            Console.Error.WriteLine("使い方は -h で確認できます。");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"I/O エラー: {ex.Message}");
            return 3;
        }
    }

    private static void AddFooter(string path)
    {
        using FileStream file = OpenForBinaryEdit(path);
        long rawSize = file.Length;
        if (rawSize == 0)
            throw new CommandLineException("空のRAWイメージにはVHDフッターを付与できません。");
        if (rawSize % VhdFooter.SectorSize != 0)
            throw new CommandLineException("RAWイメージのサイズは512バイトの倍数である必要があります。");
        if (HasVhdCookie(file))
            throw new CommandLineException("末尾にVHDフッターが既に存在します。");

        byte[] footer = VhdFooter.Create(rawSize);
        try
        {
            file.Position = rawSize;
            file.Write(footer);
            file.Flush(flushToDisk: true);
        }
        catch (Exception appendError)
        {
            try
            {
                file.SetLength(rawSize);
                file.Flush(flushToDisk: true);
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    $"フッター付与に失敗し、元のサイズへの復元にも失敗しました: {rollbackError.Message}",
                    appendError);
            }
            throw;
        }

        Console.WriteLine($"VHDフッターを付与しました: {path}");
        Console.WriteLine($"{rawSize:N0} -> {file.Length:N0} bytes");
    }

    private static void RemoveFooter(string path)
    {
        using FileStream file = OpenForBinaryEdit(path);
        if (file.Length < VhdFooter.SectorSize)
            throw new CommandLineException("ファイルがVHDフッターより小さいため処理できません。");

        long vhdSize = file.Length;
        byte[] footerBytes = ReadFooter(file);
        VhdFooter footer = VhdFooter.ParseAndValidate(footerBytes, vhdSize);

        // 検証がすべて完了してから末尾512バイトを切り詰める。
        file.SetLength(footer.CurrentSize);
        file.Flush(flushToDisk: true);

        Console.WriteLine($"VHDフッターを除去しました: {path}");
        Console.WriteLine($"{vhdSize:N0} -> {file.Length:N0} bytes");
    }

    private static bool HasVhdCookie(FileStream file)
    {
        if (file.Length < VhdFooter.SectorSize)
            return false;

        byte[] footer = ReadFooter(file);
        return VhdFooter.HasCookie(footer);
    }

    private static byte[] ReadFooter(FileStream file)
    {
        byte[] footer = new byte[VhdFooter.SectorSize];
        file.Position = file.Length - VhdFooter.SectorSize;
        file.ReadExactly(footer);
        return footer;
    }

    private static FileStream OpenForBinaryEdit(string path) =>
        new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, VhdFooter.SectorSize, FileOptions.RandomAccess);

    private static void PrintHelp()
    {
        Console.WriteLine("""
            RAW/VHD footer switcher

            Usage:
              RawVhdTool -a <image>   VHDフッターを付与（RAW -> 固定VHD）
              RawVhdTool -r <image>   VHDフッターを除去（固定VHD -> RAW）
              RawVhdTool -h           ヘルプ

            対象ファイルを直接編集します。イメージ本体のコピーは行いません。
            ファイル名と拡張子は変更しません。
            """);
    }
}

internal sealed class CommandLineException(string message) : Exception(message);
