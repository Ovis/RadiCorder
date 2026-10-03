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

改修前の本体 tests は 294 件成功。追加する回帰 tests では、DB の再オープン・既存 relation と UTC 時刻・integrity/foreign key、HLS fixture、検索順序、予約時刻、取り込み CSV、重複検出を確認します。全 tests は独立した temp DB/file を使います。実 provider の認証は国内ネットワークを使用する Canary CI の担当であり、cloud の成功と混同しません。

基準: `c8acb39fc29320748eadd207c10b9df230f476d9`。
