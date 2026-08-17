# RawVhdTool

巨大なRAWディスクイメージと固定VHDを高速に切り替える .NET 8 CLIです。
イメージ全体はコピーせず、同一ファイルの末尾にある512バイトのVHDフッターだけを
バイナリ編集で付与または除去します。

## ビルド

```console
dotnet build RawVhdTool/RawVhdTool.csproj -c Release
```

ビルド済みDLLは次のように実行できます。

```console
dotnet RawVhdTool/bin/Release/net8.0/RawVhdTool.dll -h
```

## 使い方

RAWイメージへVHDフッターを付与します。

```console
dotnet RawVhdTool/bin/Release/net8.0/RawVhdTool.dll -a disk.raw
```

固定VHDからVHDフッターを除去します。

```console
dotnet RawVhdTool/bin/Release/net8.0/RawVhdTool.dll -r disk.raw
```

処理後もファイル名と拡張子は変わりません。必要なら利用側に合わせて名前を変更してください。

## 動作と安全性

- `-a` は末尾へ512バイトだけ追記します。既にVHD cookieがある場合は二重付与を拒否します。
- `-r` はcookie、形式、固定ディスク種別、チェックサム、記録容量を検証してから512バイト切り詰めます。
- RAWのサイズはVHDのセクター境界に合わせ、512バイトの倍数である必要があります。
- 動的VHDと差分VHDには対応していません。
- 対象ファイルをほかのプロセスが開いている場合は、排他的編集のため処理を拒否することがあります。

このツールは対象ファイルを直接変更します。重要なイメージは事前にバックアップしてください。
