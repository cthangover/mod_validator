# Validator — プレイせずに MOD を検査する

Validator は、実行中のゲームのすべての MOD——マニフェスト、データファイル、シナリオ、シーンパッチ、C# ソース——を読み、壊れているか怪しいものを報告します。誰も宣言していないアイテム・シーン・背景への参照、不明なシナリオコマンド、誤ったパラメーター、不足した依存関係。ゲームが始まるたびにバックグラウンドで走り、レポートを書くので、タイプミスはプレイヤーがその行に到達する前に見つかります。

検査するのはファイルに見えるものだけです。Validator はゲームをプレイしません: 受け入れられたシナリオでも、作者の意図どおりに動くとは限らず、実行時に id を組み立てるコードは追えません。

## 誤りとその修正

プレイヤーに狼の肉を与えるシナリオの行。アイテム id にタイプミスがあります:

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

次に Cthangover を起動すると、レポートにこれが入ります:

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

MOD、ファイル、行が誤りを指し示します。`food/wolf_meat` に直せば問題は消え、概要のエラー数がひとつ減ります（この実行では 18 から 17 へ）。

## 実行のしかたとレポートの読み方

- それを含むゲームに入ると（Cthangover は開発者ツールと一緒に同梱します）、MOD の読み込みのあとで自動的に始まり、バックグラウンドスレッドで走ります。ゲームは止まりません。
- ログには一巡ごとに一行入ります: `validator: started, 33 mod(s) queued`、そのあとにエラー・警告・情報の合計。
- レポートは `user://validation/report.json`（ゲームのユーザーデータフォルダー）に書かれます。概要、バージョンとチェックサム付きの検査済み MOD、そして問題の一覧が入ります。

問題ごとに:

- `severity` — `error`（壊れている）、`warning`（おそらく誤り）、`info`（見ておく価値がある。たとえばどこからも参照されないアイテム）;
- `code` — 問題の種類。たとえば `REF_NOT_FOUND`、`JSON_PARSE_ERROR`、`SCHEMA_MISSING_FIELD`、`SCHEMA_UNKNOWN_PARAM`、`DECL_UNUSED`、`DYNAMIC_KEY`;
- `mod`、`file`、`line` — どこを直すか; `suggestion` — 何をすればいいか。

`DYNAMIC_KEY` の情報は、キーが実行時に組み立てられるため Validator が検査できないことを意味します。エラーではありません。

## 何を検査するか

検査は三つのフェーズで走ります:

1. **スキーマ** — `manifest.json` に文字列の `id` と `version` がある; JSON ファイルが解釈できる。
2. **契約** — 各 MOD が何を宣言し、何を参照するか: あらゆる種類のエンティティ（キャラクター、アイテム、クエスト、スキル、レシピ、行動、そしてスキーマが記述した種類）、シナリオのコマンドと行動とそのパラメーター、シーンパッチ、C# コードが読み書きするプロパティ。
3. **突き合わせ** — すべての参照に宣言がある、`depends` が他の MOD から使うものを覆っている、誰にも使われない宣言の一覧。

シナリオの行動とパラメーターは、MOD が同梱するスキーマに対して（Schemas を参照）、コマンドパレットと同じ規則で検査されます。

## 設定

Modマネージャーのこの MOD の設定タブで:

- **Enabled**、**Run on start** — 一巡を止める;
- **Parse C#**、**Parse GDScript**（既定ではオフ、遅い）、**Parse scenarios** — どのソースを読むか;
- **Write report**、**Report path** — `report.json` の行き先;
- **Block mods on error** — 設定にはありますが、現行バージョンでは何も変わりません: MOD が止められることはありません。

## うまくいかないとき

1. **レポートがない。** 一巡が終わる前にゲームが閉じられた（Cthangover では 30 秒ほど）か、**Write report** がオフです。
2. **`DYNAMIC_KEY` の行が多い。** キーを組み立てるコードでは普通のことです。情報扱いです。
