# 動作を維持する責務分離

この改修は「RadiCorder ドメイン再設計・Canary 統合検証計画」を基に、既存の動作を維持したまま変更箇所を見つけやすくするリファクタリングです。Web、業務判断、外部 I/O を分けます。MVC の Controller / View と既存 Minimal API を維持し、専用のクリーンアーキテクチャ用 project は追加しません。

## 維持する契約

- URL、HTTP method、応答形式、SignalR、画面と配布形式。
- 検索の並び順・同順位の優先順・100件制限、予約の優先順位とタグ規則。
- 録音開始時刻、retry 回数、失敗メッセージ、状態更新・通知・file 操作の順序。
- DB entity、enum の値、列・制約・index、migration と時刻変換。
- 設定のキー、読み込み優先順位、保護キーと認証情報の暗号化 purpose。
- 公開 logic の型・constructor。Canary の現在の project 参照を壊さない。

計画に記載した offline 起動、terminal state の変更、journal、execution key、タグの claim snapshot、新しい修復判定は動作や DB を変えるため、この PR に含めません。保存後 DB 障害などの既存の課題も、今回の機械的な責務分離と混ぜずに扱います。

## 検証

改修前の本体 tests は 294 件成功。追加する回帰 tests では、DB の再オープン・既存 relation と UTC 時刻・integrity/foreign key、HLS fixture、検索順序、予約時刻、取り込み CSV、重複検出を確認します。追加するDB/fileの回帰 tests は独立した temp DB/file を使います。実 provider の認証は国内ネットワークを使用する Canary CI の担当であり、cloud の成功と混同しません。

基準: `c8acb39fc29320748eadd207c10b9df230f476d9`。

## 変更後の構成

| 責務 | 実装 |
|---|---|
| ASP.NETの起動とmiddleware順序 | `RadiCorder/Program.cs` |
| MVC・API・Hubのルート登録 | `RadiCorder/Hosting/EndpointRouteBuilderExtensions.cs` |
| Linux初回設定・DBバックアップとmigration | `RadiCorder/Hosting/*Initializer.cs` |
| 業務サービス・外部I/O・WorkerのDI | `RadiCorder.Logics/DependencyInjection/LogicServiceCollectionExtensions.cs` |
| Webの設定・logging・SignalR publisherのDI | `RadiCorder/DependencyInjection/ServiceCollectionExtensions.cs` |
| 検索のHTTP応答 | `RadiCorder/Features/Program/ProgramEndpoints.cs` |
| 利用可能な局での検索・結果統合 | `ProgramSearchService` / `ProgramSearchResultBuilder` |
| radiko proxyのHTTP入出力 | `RadikoStreamingEndpoints` |
| 配信取得・ライブ開始同期・playlist整形 | `Services/Streaming/RadikoPlaylistClient` / `RadikoPlaylistProcessor` |
| スケジューラの監視・キュー投入・復旧 | `RecordingScheduleBackgroundService` |
| ジョブ単位の状態更新と実行 | `RecordingJobExecutor` |
| 録音フローと呼び出しごとの状態・後処理 | `RecordingOrchestrator` / `RecordingExecutionContext` |
| 時刻・失敗分類・予約優先順位の判断 | `RecordingScheduleTiming` / `RecordingJobErrorClassifier` / `KeywordReservationPolicy` |
| 外部取込のscan・検証・DB transaction | `ExternalRecordingImportLobLogic` |
| CSV・管理下path・template・音声metadata | `Infrastructure/Import/*` |
| 重複候補の取得・処理順序 | `RecordedProgramDuplicateDetectionService` |
| 比較対象の選択と類似度の計算 | `Domain/DuplicateDetection/*` |
| ffmpegによる音声指紋の取得 | `RecordingAudioFingerprintReader` |
| 設定の公開状態・機能別の更新 | `AppConfigurationService` と同名の機能別partialファイル |
| 設定テーブルの値の取得・保存 | `Infrastructure/Configuration/AppConfigurationValues.cs` |

`AddLogicDiCollection` は既存の本体構成を維持する入口です。共通の `AddRadiCorderLogics` はWorkerを起動せず、`AddRadiCorderBackgroundServices` がWorkerを登録します。DB、設定、公開イベントの実装はhostが登録します。singleton/scoped/transient、HTTP client名と15秒timeoutは維持しています。Canaryの現行手動compositionをこのPRで切り替えてはいません。

Controllerは引き続きViewを返し、Minimal APIはHTTPの入力・応答を扱います。大きなクラスを機械的に別projectへ移す構成にはしていません。設定は公開interface・lock・scope・transaction・初期化順序を維持するためpartialファイルに分け、永続値の操作を別実装へ抽出しました。

## 実装後の検証結果

- 本体logic tests: **358件成功**。改修前の294件を含みます。
- Web/integration tests: **18件成功**。合計 **376件成功、失敗0、skip0**。
- 実hostで生成した全OpenAPI documentが、改修前の固定fixtureと一致。可変のserver URLのみ比較から除外しています。
- MVCの主要画面、proxyの2ルート、credential入力エラー、ライブmaster/media解決、binary中継、上流HTTPエラーを確認。
- 実SQLiteと実storageを通したRadiko/RadiruのImmediate・TimeFree・OnDemandジョブの成功/失敗、録音結果、保存file、retry回数を確認。provider取得とtranscodeはfakeを使用し、実サービスの成功や音声decodeを保証するtestにはしていません。
- 最新migrationの再適用、旧DBのWALを含むbackup、再オープン、relation、nullable/non-nullable UTC日時、integrity/foreign key checkを確認。
- 資格情報の2項目目でDB保存を失敗させ、transactionのrollbackと公開状態・資格情報の保持を確認。
- 旧Logics assemblyの公開198型と改修後217型を比較し、既存の公開memberの削除・signature変更は0件。
- 現行Canaryのソースを一時checkoutで改修後Logicsへ参照しbuild成功。既存のNU1510 package警告あり。Canary本体の追跡ファイルとsubmodule参照は変更していません。
- 本体Release build / framework-dependent publish成功。Windows・Linux各配布先での実機起動、および国内providerの実通信は未実行です。
- DB entity・migration・enum、View、TypeScript、生成assets、npm lock、設定ファイル、installerには差分なし。

CIは既存のnpm準備と本体buildに続けて、`dotnet test --configuration Release RadiCorder.slnx` で両test projectを実行します。各testの追加temp DBはユーザーのDBと分離しています。Web testsは本番と同じルート登録を使用し、StartupTaskと自動Workerは起動しません。

## 後続の改修方法

新しい業務判断は、現在時刻や比較対象などを引数で受け取る規則として追加し、DB・HTTP・processなしで検証します。Webのhandlerは結果をHTTPへ変換し、外部I/Oはadapterで扱います。入力制限、比較係数、retryや状態遷移を変える場合は、このPRのような責務移動とは別に仕様変更を明示してください。

録音の保存後DB障害、cancel/timeoutの型による区別、host停止時の実行task管理、active jobの一意制約、providerの部分失敗、offline起動は、元の計画にある次の仕様変更候補です。今回の成功件数だけでそれらが修正済みとは判断しません。新しいmigrationや復旧手順が必要な改修では、旧DB fixtureとbackupからの復元を先に確認します。
