# Third-Party Notices

このファイルは RadiCorder で利用している主要なサードパーティライブラリと、そのライセンス情報の一覧です。  

## NuGet

| Package | License | URL |
|---|---|---|
| AngleSharp | MIT | https://github.com/AngleSharp/AngleSharp |
| CsvHelper | MS-PL OR Apache-2.0 | https://github.com/JoshClose/CsvHelper |
| Microsoft.AspNetCore.DataProtection | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.EntityFrameworkCore | MIT | https://github.com/dotnet/efcore |
| Microsoft.AspNetCore.OpenApi | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.OpenApi | MIT | https://github.com/microsoft/OpenAPI.NET |
| Microsoft.Extensions.Hosting.Systemd | MIT | https://github.com/dotnet/runtime |
| SQLitePCLRaw.bundle_e_sqlite3 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| Microsoft.EntityFrameworkCore.Sqlite | MIT | https://github.com/dotnet/efcore |
| Microsoft.Extensions.Http | MIT | https://github.com/dotnet/runtime |
| TagLibSharp | LGPL-2.1-only | https://github.com/mono/taglib-sharp |
| Ulid | MIT | https://github.com/Cysharp/Ulid |
| ZLogger | MIT | https://github.com/Cysharp/ZLogger |

## Node.js

| Package | License | URL |
|---|---|---|
| hls.js | Apache-2.0 | https://github.com/video-dev/hls.js |
| @microsoft/signalr | MIT | https://github.com/dotnet/aspnetcore |
| sass | MIT | https://github.com/sass/dart-sass |
| DOMPurify | Apache-2.0 OR MPL-2.0 | https://github.com/cure53/DOMPurify |
| tailwindcss | MIT | https://github.com/tailwindlabs/tailwindcss |
| typescript | Apache-2.0 | https://github.com/microsoft/TypeScript |

## テスト/開発時依存

| Package | License | URL |
|---|---|---|
| jsdom | MIT | https://github.com/jsdom/jsdom |
| Microsoft.NET.Test.Sdk | MIT | https://github.com/microsoft/vstest |
| Moq | BSD-3-Clause | https://github.com/devlooped/moq |
| NUnit | MIT | https://github.com/nunit/nunit |
| NUnit.Analyzers | MIT | https://github.com/nunit/nunit.analyzers |
| NUnit3TestAdapter | MIT | https://github.com/nunit/nunit3-vs-adapter |

## 開発用依存の監査結果（2026-10-04）

`npm audit --omit=dev` は検出0件。`ts-node` は未使用のため削除し、実行時の `ws` と更新可能な開発用依存を修正版へ更新した。

開発用Tailwind CSS 3の依存 `braces` 3.0.3には [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) が残る。監査では伝播先を含む5パッケージをhighと表示するが、原因のアドバイザリはこの1件。確認時点のnpmレジストリでは修正版が提供されていない。深く入れ子になったglobパターンによるビルド処理のDoSが対象で、Webサーバーやブラウザにはこのライブラリを配布しない。ビルド設定はリポジトリ内の固定パターンを使用する。

通常の表示を保つため、このPRではTailwind 4への移行を行わない。`braces`の修正版公開後にlockfileを更新するか、別PRで画面の表示差分を確認してTailwind 4へ移行する。
