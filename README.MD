# RadiCorder

RadiCorder は、インターネットラジオ（radiko / らじる★らじる）の録音・再生・管理を行う個人向けWebアプリです。  
ブラウザから操作でき、PC・スマホの両方で利用できます。

## できること

- 放送中番組の再生
- アプリ内のページ移動や戻る・進むの間も再生を継続（設定で有効化した場合）
- リアルタイム録音 / タイムフリー録音（radiko対応番組のみ） / 聞き逃し配信録音（らじる★らじる対応番組のみ）
- 番組表の確認（radiko / らじる★らじる）
- キーワード自動予約
- 録音済み番組の再生・ダウンロード・削除
- タグによる整理と検索
- 未視聴表示（再生またはダウンロードで視聴済みに切り替え）
- お知らせ通知
- 既存音声ファイルの外部取り込み

## 基本的な使い方

1. ホーム画面
- 現在放送中の番組を確認
- 再生 / リアルタイム録音 / タイムフリー録音を実行
- エリアタブで表示対象を切り替え（検索時は全エリア横断）

2. 番組表・検索
- 放送局 / 日付 / キーワードで番組を検索
- 録音予約
- キーワード自動予約の登録
- 放送終了後かつ対応番組では「聞き逃し配信録音」が可能（らじる★らじる）

3. 録音予定・自動予約ルール
- 登録元（番組表予約 / 自動予約ルール）や録音種別を確認
- 録音予定詳細で登録元ルールと付与予定タグを確認
- キーワード自動予約ルールの優先度・タグ付与方式を調整

4. 録音済み一覧
- 再生 / ダウンロード / 削除
- タグ追加・絞り込み・一括操作
- 連続再生、ジャンプ操作（秒/分単位）

5. 設定・メンテナンス
- 保存先、ポート、通知設定、radikoログイン設定を管理
- 既存録音の取り込み（CSV入出力を含む）
- データ整合性チェックや不要ファイルのメンテナンスを実行

## 動作環境

- Windows 11
- Linux（x64 / arm64）
  - arm64の動作は未検証です
- Docker

## 開発（ソースから実行）

前提:
- .NET SDK 10.0
- Node.js / npm
- ffmpeg

初回セットアップ:

```powershell
cd .\RadiCorder
npm install
```

ビルド:

```powershell
npm run build
dotnet build ..\RadiCorder.slnx -m:1 -v:m
dotnet test ..\RadiCorder.slnx -m:1 -v:m
```

補足:
- `npm run build` 実行時に `hls.js` を `node_modules` から `wwwroot/lib/hls.js/hls.min.js` へ同期します（CDNは使用しません）。
- 実行中の `RadiCorder.exe` があると `dotnet build` が失敗することがあります。必要に応じて先に停止してください。

## 配布物

リリースページの Assets から、環境に合った配布物を利用してください。

- Windows ZIP（x64）
  - `RadiCorder-<version>-win-x64.zip`
- Linux ZIP（x64 / arm64）
  - `RadiCorder-<version>-linux-x64.zip`
  - `RadiCorder-<version>-linux-arm64.zip`
- Windows インストーラー
  - `RadiCorder-setup-<version>-fd.exe`（Framework-dependent）
  - `RadiCorder-setup-<version>-sc.exe`（Self-contained）
- Linux DEB（amd64 / arm64, fd / sc）
  - `radicorder_<version>_amd64-fd.deb`
  - `radicorder_<version>_arm64-fd.deb`
  - `radicorder_<version>_amd64-sc.deb`
  - `radicorder_<version>_arm64-sc.deb`
- Docker イメージアーカイブ
  - `RadiCorder-docker-<version>-linux-amd64.tar`

## インストール（Windows）

### 1. インストーラーを使う（推奨）

1. `RadiCorder-setup-...exe` を実行
1. ウィザードでポート・録音保存先・一時ファイル保存先を指定
1. 完了後、表示された URL にアクセス

補足:
- `fd` は .NET Runtime が必要です。
- `sc` はランタイム同梱のため .NET Runtime 不要です。

### 2. ZIP版を使う

ZIP展開後、同梱 `scripts/windows/install.ps1` を管理者権限で実行してください。

```powershell
pwsh .\scripts\windows\install.ps1 -HttpPort 8085
```

主な既定値:
- インストール先: `$env:LOCALAPPDATA\RadiCorder`
- 起動方式: タスクスケジューラ（`RadiCorder` タスク）
- バインド: `0.0.0.0:<指定ポート>`

## インストール（Linux / Debian系）

### 1. DEBパッケージ

```bash
sudo apt install ./radicorder_<version>_<arch>-<variant>.deb
```

`variant`:
- `sc`: Self-contained（ランタイム不要）
- `fd`: Framework-dependent（ランタイム必要）

`fd` では以下依存関係を要求します（`Depends`）:
- `dotnet-runtime-10.0`
- `aspnetcore-runtime-10.0`
- `ffmpeg`

初回インストール時に設定:
- ポート番号
- 録音保存先
- 一時ファイル保存先

systemd:
- サービス名: `radicorder.service`
- 有効化/起動はインストール時に実行されます

確認コマンド:

```bash
sudo systemctl status radicorder.service --no-pager -l
```

### 2. アンインストール（Linux）

```bash
# 設定ファイルを残して削除
sudo apt remove radicorder

# 設定ファイルも含めて完全削除
sudo apt purge radicorder
```

## Docker

### GHCRから利用

```bash
docker pull ghcr.io/ovis/radicorder:<version>
docker run -d --name radicorder -p 8085:8085 ghcr.io/ovis/radicorder:<version>
```

### tarアーカイブから利用

```bash
docker load -i RadiCorder-docker-<version>-linux-amd64.tar
docker run -d --name radicorder -p 8085:8085 ghcr.io/ovis/radicorder:<version>
```

補足:
- Docker イメージには ASP.NET ランタイムと ffmpeg が含まれます。

## 最初にやる設定

画面右上の設定から、まず次を確認してください。

- 番組の保存先フォルダ
- 一時ファイル保存先
- 録音開始/終了マージン
- 通知設定
- radikoログイン設定（必要な場合）

重要:
- radikoログイン情報は Data Protection を利用して暗号化保存していますが、本アプリは個人利用向けの簡易実装であり、厳格な秘密情報保護を保証するものではありません。

## 起動前設定（保存先ルート）

保存先ルートと一時保存先ルートは、起動前に設定します。

`radicorder.settings.sample.json` を `radicorder.settings.json` として配置し、必要に応じて編集してください。

補足:
- Linux では、`radicorder.settings.json` と `/etc/radicorder/radicorder.settings.json` の両方が存在しない場合、初回起動時に実行ディレクトリへ `radicorder.settings.json` を自動生成します。

例（`radicorder.settings.json`）:

```json
{
  "RadiCorder": {
    "RecordFileSaveFolder": "D:\\RadiCorder\\record",
    "TemporaryFileSaveFolder": "D:\\RadiCorder\\temp"
  }
}
```

環境変数で上書きする場合:
- `RadiCorder__RecordFileSaveFolder`
- `RadiCorder__TemporaryFileSaveFolder`

補足:
- Web UIで変更できる設定値はDB（`AppConfiguration`）に保存され、起動後はそちらが優先されます。

## 保存データとログ

代表的な保存先:
- `db` : アプリのデータベース
- `record` : 録音ファイル
- `temp` : 一時ファイル
- `logs` : ログ
- `keys` : Data Protection キー

## トラブルシューティング

- 録音できない
  - ffmpeg が導入されているか確認
  - 保存先フォルダへの書き込み権限を確認
- 番組が取得できない
  - ネットワーク接続を確認
  - API側の一時障害の可能性があるため時間を置いて再実行
- 再生できない
  - 録音ファイル実体が存在するか確認
  - 設定画面のメンテナンス機能で欠損チェックを実行

## 注意事項

- 本アプリは個人利用を想定しています。
- 録音・利用は各サービスの利用規約および関連法令の範囲で行ってください。
- 本アプリはローカル環境での利用を前提としており、意図的に認証機能を設けていません。
- 本アプリをインターネットへ外部公開しないでください。公開形態によっては著作権法違反となる可能性があります。
- `radiko` や `NHKらじる★らじる` 側の仕様変更により、再生・録音・番組取得などの機能が予告なく利用できなくなる場合があります。
- 各サービスの利用規約および関連法令の確認・遵守は利用者自身の責任で行ってください。
- 本アプリの利用によって生じたいかなる損害・不利益・トラブルについても、開発者は責任を負いません。
