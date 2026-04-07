# Curia Wiki 仕様更新（実装準拠 + 機能拡張）

## 1. 目的

本書は、現状コード（`WikiService` / `WikiIngestService` / `WikiQueryService` / `WikiLintService` / `WikiViewModel`）を基準に、Wiki 機能の実態を反映した仕様へ更新する。

あわせて次の機能追加を定義する。

- Wiki の追加カテゴリをユーザーが拡張できること
- Import / Query / Lint のプロンプトを調整できること

---

## 2. 現状仕様（As-Is: コード実態）

### 2.1 初期化時の構成

`WikiService.InitializeWiki` は `wiki/<domain>/` に以下を生成する（既存ファイルは上書きしない）。

- `raw/`
- `pages/sources/`
- `pages/entities/`
- `pages/concepts/`
- `pages/analysis/`
- `wiki-schema.md`
- `AGENTS.md`
- `CLAUDE.md`（内容: `@AGENTS.md`）
- `index.md`
- `log.md`
- `.wiki-meta.json`

### 2.2 スキーマ/ガイドの読み込み実態

- LLM 呼び出しで読み込んでいるのは `wiki-schema.md` のみ。
  - `WikiIngestService.GenerateIngestProposal`
  - `WikiQueryService.Query`
- `AGENTS.md` / `CLAUDE.md` は生成されるが、Wiki の LLM プロンプトには未使用。
- 毎回呼び出し直前に `wiki-schema.md` を再読込している（ホットリロード相当）。

### 2.3 カテゴリ実態

- 画面表示カテゴリ（Pages タブ）は固定 4 種:
  - `sources`
  - `entities`
  - `concepts`
  - `analysis`
- カテゴリ判定（`WikiService.GetCategoryFromPath`）も固定 4 種。
- `pages/<任意カテゴリ>/...` のファイル自体は保存可能（`SavePage` はディレクトリを自動作成）。
- ただし固定カテゴリ以外は `Category="root"` 扱いになり、Pages タブのカテゴリ表示には出ない。

### 2.4 Import 時の path 制約実態

- 更新候補選定（1回目 LLM）では `pages/*.md` 配下のみ許可し、既存ページに限定。
- 最終反映（2回目 LLM 結果の適用）では `newPages` / `updatedPages` の `path` に対する厳密バリデーションは未実装（空文字チェック中心）。
- そのため仕様上は `pages/` 配下以外を書けてしまう余地がある。

### 2.5 プロンプト調整の実態

- Wiki専用のプロンプト設定 UI / 設定項目は未実装。
- 全体設定として以下は有効:
  - `LlmLanguage`（出力言語指示）
  - `LlmUserProfile`（`LlmClientService` が全 system prompt の先頭に注入）
  - `LlmParameters`（temperature 等の API パラメータ）

---

## 3. 追加要件（To-Be）

### 3.1 要件A: カテゴリ拡張機能

#### A-1. カテゴリ定義ファイル

`wiki/<domain>/.wiki-categories.json` を追加する。

```json
{
  "version": 1,
  "categories": [
    "sources",
    "entities",
    "concepts",
    "analysis"
  ]
}
```

- 未存在時は上記デフォルトを生成。
- `sources` は必須カテゴリ（削除不可・リネーム不可）。
- カテゴリ名の禁止条件:
  - 空文字
  - `.` / `..`
  - セパレータ文字（`/`, `\`）
  - Windows 禁止文字（`< > : " | ? *`）
  - Windows 予約名（`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`）
  - 末尾の `.` または空白
  - 制御文字（U+0000-U+001F）
- カテゴリ名は保存時に `ToLowerInvariant()` で小文字へ正規化する。
- 重複判定は大文字小文字を区別しない（`OrdinalIgnoreCase`）。
- UI 表示名も論理名（小文字）を正とする。
  - 入力時に大文字が含まれていても、保存確定時に小文字表示へ統一する。
  - 「表示名のみ元入力を保持する」挙動は採用しない。
- `.wiki-categories.json.version` は将来拡張のための互換管理キーとする。
  - `version=1` のみ読込対象とする。
  - 未知バージョン（`version!=1`）は自動上書きせず、Wiki ドメイン全体を読み取り専用モードで起動し、移行未対応である旨を UI に表示する。

#### A-1.1 カテゴリ編集操作（追加/削除/リネーム）

- 追加: 正規化・バリデーション後に `.wiki-categories.json` へ保存。
- 削除: `.wiki-categories.json` からのみ削除する（既存 `pages/<category>/` は自動削除しない）。
  - 注: 実ディレクトリが残る場合でも、削除カテゴリは A-3 の保存対象にはならない（表示のみ）。
  - 完全に無効化したい場合は、利用者が `pages/<category>/` を別名へ移動または削除する。
- リネーム: `pages/<old>/` を `pages/<new>/` へ移動する。
  - `sources` はリネーム禁止。
  - `pages/<old>/` が存在しない場合は、ディレクトリ移動を伴わず `.wiki-categories.json` 上のカテゴリ名のみ変更し、`pages/<new>/` は自動作成しない（初回保存時に作成）。
    - このケースでは `oldPath/newPath/tempPath` はジャーナルに記録するが、実ファイル移動フェーズ（`moving_to_temp`, `moving_to_new`）には遷移しない。
    - 設定更新失敗時はファイル補償移動を行わず、`.wiki-categories.json` を旧値へ戻して終了する（no-op ロールバック）。
  - 移動失敗時は `.wiki-categories.json` 更新を取り消し、部分適用を禁止する。
  - さらに、移動成功後に `.wiki-categories.json` 更新が失敗した場合は `pages/<new>/ -> pages/<old>/` の補償移動を必須とし、設定と実ディレクトリの不整合を残さない。
  - `pages/<new>/` が既に存在する場合は自動マージしない（衝突として失敗）。
  - 実装は 2 段階で行う（例: `old -> temp -> new`）。失敗時は `temp -> old` を試行し、最終的に「旧状態維持」か「要手動復旧」を明示して終了する。
  - `temp` は衝突回避のため `.<old>.rename-tmp-<GUID>` 形式とする。
- リネーム開始時に `.wiki-rename-txn.json`（`oldCategory`, `newCategory`, `oldPath`, `newPath`, `tempPath`, `oldExistedAtStart`, `phase`）を作成し、復旧判定の正本とする。
  - `oldExistedAtStart` はリネーム開始時点の `pages/<old>/` 実在有無を保持し、起動時復旧分岐の正本として扱う。
  - 起動時に `*.rename-tmp-*` または `.wiki-rename-txn.json` を検出した場合は、前回リネーム中断とみなし、`.wiki-rename-txn.json` の `phase` に基づいて `temp -> old` / `temp -> new` / `new -> old` のいずれかを一意に選択して自動復旧を試行する。ジャーナル欠損時は安全側（`old` 復元優先）で扱う。
  - 復旧完了時のみ `.wiki-rename-txn.json` と一時ディレクトリを削除する。
  - `.wiki-rename-txn.json.phase` は以下の列挙値のみ許可する。
    - `prepared`（ジャーナル作成直後）
    - `moving_to_temp`（`old -> temp` 実行中）
    - `moving_to_new`（`temp -> new` 実行中）
    - `updating_config`（`.wiki-categories.json` 更新中）
    - `compensating`（`new -> old` 補償移動中）
    - `committed`（最終確定）
    - `rolled_back`（旧状態復帰確定）
  - `phase` は単調前進のみ許可し、各フェーズ遷移は「ディスク反映前に phase 永続化」してから実ファイル操作を行う。
- 起動時復旧は `phase` を唯一の正本として経路選択する。
  - `prepared`: 実ファイル操作未実施として扱い、`old/new/temp` の実在を点検したうえで `old` 維持を正とする。`new` や `temp` が残存している異常ケースでは `new -> old` または `temp -> old` を優先し、収束後に後片付けを行う。
  - `moving_to_temp` / `moving_to_new`: `old` 復元優先（必要に応じて `temp -> old` または `new -> old`）
  - `updating_config`: 設定ファイル状態と実ディレクトリ状態を突き合わせ、最終的に「設定=old・実体=old」へ収束させる。
      - `new -> old` を実行した場合は `.wiki-categories.json` も必ず旧値へ補償更新する。
      - いずれか片側のみ復旧した状態で確定させない（不整合状態を残さない）。
  - `compensating`: 補償継続（`new -> old`）を再試行し、失敗時は手動復旧案内
  - `committed`: 後片付けのみ（ジャーナル/一時ディレクトリ削除）
  - `rolled_back`: 後片付けのみ（ジャーナル/一時ディレクトリ削除）
  - ただし `oldExistedAtStart=false` のリネーム（設定名のみ変更ケース）では、起動時復旧で `new -> old` / `temp -> old` の実ファイル移動を行わない。設定ファイルを旧値へ戻す no-op ロールバックを最優先し、実ディレクトリは非変更とする。
  - `new -> old` / `temp -> old` 復旧時に `oldPath` が既存の場合は上書き復旧を禁止する（自動マージ禁止）。
    - 既存 `oldPath` は `<old>.conflict-<yyyyMMdd-HHmmss-fff>-<GUID>` へ退避後、`newPath` または `tempPath` を `oldPath` へ戻す。
    - 退避/復旧のいずれかに失敗した場合は自動処理を中断し、衝突ディレクトリ一覧と手動復旧手順を提示する（データ消失回避を優先）。

#### A-1.2 既存ディレクトリとの整合（移行）

- `.wiki-categories.json` 初回生成時は、デフォルト4カテゴリに加えて `pages/` 直下の既存ディレクトリ（小文字正規化後）を自動取り込みする。
  - これにより既存Wikiで運用中のカテゴリが「初回アクセス直後に保存不可」になる事態を回避する。
  - 競合（同一正規化名への多重対応）がある場合は自動取り込みを中止し、解消ガイドを表示したうえで読み取り専用で起動する。
- `.wiki-categories.json` 初回生成/読込時、`pages/` 配下実ディレクトリ名はカテゴリ比較時に小文字正規化して扱う。
- 既存に `pages/Tables/` のような大文字混在ディレクトリがある場合:
  - 表示カテゴリ名は `tables` として扱う（論理名は小文字）。
  - 物理ディレクトリ名の即時変更は必須としない（Windows 互換維持）。
  - カテゴリリネーム操作を実行した場合のみ、移動先の物理名を新カテゴリ名（小文字）へ揃える。
- 同一カテゴリに正規化される複数ディレクトリ（例: `Tables` と `tables`）を検出した場合は競合として編集操作を拒否し、利用者に解消を促す。

#### A-2. Pages タブ表示

- 固定配列を廃止し、`.wiki-categories.json` を基準にカテゴリ表示。
- さらに `pages/` 配下の実ディレクトリをスキャンし、定義外ディレクトリも表示対象に含める。
  - 例: `pages/tables/` が手動追加されていれば表示する。
- 表示順:
  1. `sources`
  2. 設定ファイル順
  3. 未定義ディレクトリ（名前昇順）
- 表示用カテゴリ集合は論理名（小文字）で一意化する。
  - `sources` は常に先頭に1回のみ表示し、設定側の重複列挙は無視する。
  - 設定カテゴリと未定義ディレクトリが同名（正規化後一致）の場合、設定カテゴリとして扱い未定義側には重複表示しない。
  - 表示名は常に小文字で統一し、入力時の大文字小文字ゆれは表示へ持ち込まない。

#### A-3. Import 保存時バリデーション

- `newPages` / `updatedPages` の `path` は `pages/<category>/<name>.md` のみ許可。
- `index.md` / `log.md` は専用フィールド（`indexUpdate`, `logEntry`）でのみ更新可能。
- 不正 path は保存拒否し、ユーザーへ理由表示。
- `<category>` は `.wiki-categories.json` に定義されたカテゴリのみ許可。
  - `pages/` 直下の実在ディレクトリであっても、未定義カテゴリは保存対象にしない。
  - カテゴリ削除後に `pages/<category>/` が残っている場合も、同カテゴリへの保存は拒否する。
  - 比較は `<category>.ToLowerInvariant()` で正規化した論理名で行い、`pages/Tables/x.md` のような大文字混在入力も定義済み `tables` として判定する。
- カテゴリディレクトリ未存在時は自動作成（`.wiki-categories.json` 定義済みカテゴリに限る）。
- `<name>` は 1 セグメントのみ許可（`/` や `\` を含むサブディレクトリは禁止）。
- `<name>` の禁止条件:
  - 空文字
  - `.` / `..`
  - Windows 禁止文字（`< > : " | ? *`）
  - Windows 予約名（`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`）
  - 末尾の `.` または空白
  - 制御文字（U+0000-U+001F）
- path バリデーションは以下の 2 段で行う。
  1. 文字列レベル:
     - 入力 path は検証前に `\` を `/` へ正規化する。
     - `Path.IsPathRooted(path)` が `true` の場合は拒否する（例: `C:\...`, `\\server\share\...`）。
     - 正規化後に `pages/<category>/<name>.md` 形式を満たすこと（拡張子判定は大文字小文字を区別しない）。
     - 拡張子は保存時に `.md` へ正規化して `targetPath` を確定する。
  2. 実パスレベル: 正規化後の絶対パスが `wiki/<domain>/pages/` 配下であること
     - `Path.GetFullPath` で検証し、配下外は拒否する（例: `pages/../../raw/x.md`）
     - 比較時は `pagesRoot` を末尾セパレータ付き絶対パスに正規化して `OrdinalIgnoreCase` で判定する（Windows 想定）。
     - `pages2/...` のような prefix 偽装を防ぐため、`startsWith(pagesRootWithSeparator)` で判定する。
     - `wiki/<domain>/pages/` を起点に、`<category>` ディレクトリ到達までの区間に reparse point（junction/symlink）が存在する場合は保存拒否する（論理配下と物理配下の不一致防止）。
     - `wiki/<domain>/` より上位階層に存在する reparse point は判定対象外とする（環境依存の合法構成を阻害しないため）。
     - 絶対パス入力（例: `C:\...`）は文字列レベルで拒否する。
- 保存適用は原子的に扱う（バリデーション + 書き込み）。
  - `newPages` / `updatedPages` を全件事前検証し、1件でも不正があれば保存処理を開始しない。
  - `newPages` / `updatedPages` / `indexUpdate` / `logEntry` を含む全更新対象の `targetPath` は、正規化後に一意でなければならない（重複が1件でもあれば全保存を中止）。
    - `targetPath` 一意判定は `Path.GetFullPath` 後の絶対パスを `OrdinalIgnoreCase` で比較する（Windows 前提）。
  - 事前検証通過後は、`pages/` 配下だけでなく `index.md` / `log.md`（更新対象時）も含めた全対象のコミット計画（`entryType=create|update`, `targetPath`, `tempPath`, `backupPath`）を `.wiki-txn.json` に記録してから `File.Replace` / `File.Move` を実行する。
  - 既存ファイル更新は `File.Replace`、新規ファイル作成（`newPages`）は `temp` から `File.Move` で反映する。新規作成はバックアップ対象外とし、ロールバック時は「作成ファイルの削除」で復元する。
    - `entryType=create` のロールバック削除は、トランザクション内で作成された同一ファイルであることを識別できた場合に限定する（例: `temp` 書込時ハッシュ/サイズ+mtime を記録し一致確認）。
    - 一致確認できない場合（外部作成・外部変更の疑い）は自動削除せず `*.orphan-<GUID>` へ隔離し、手動確認導線を表示する。
  - `indexUpdate` / `logEntry` の対象ファイル（`index.md`, `log.md`）が欠損している場合は、`entryType=create` として同一トランザクション内で再生成して反映する（欠損のみを理由に全体エラー化しない）。
  - `tempPath` / `backupPath` は各 `targetPath` と同一ディレクトリ配下（同一ボリューム）に作成する。共通テンポラリ領域（別ドライブ）は使用しない。
  - `tempPath` / `backupPath` 命名は衝突回避のため `<file>.tmp-<GUID>` / `<file>.bak-<GUID>` 形式とする。
  - 全対象の反映が完了した時点で `phase=committed` を永続化し、その直後に `.wiki-txn.json` を削除する（削除失敗時は次回起動で `phase=committed` を検出して安全に削除再試行する）。
  - 途中で I/O エラーが発生した場合は `.wiki-txn.json` を基にロールバックを試行し、全ファイルの旧内容復元を優先する。
  - 次回起動時に `.wiki-txn.json` が残存していた場合は未完了トランザクションとみなし、再ロールバックまたは再コミットを試行して整合を回復する。
  - 例外的に OS 要因で完全復旧できない場合は、失敗ファイル一覧・退避先・手動復旧手順を表示する。

#### A-4. 互換性

- 既存環境は `.wiki-categories.json` 不在でも動作し、初回アクセスで自動生成。
- 既存 4 カテゴリ運用はそのまま維持。

#### A-5. AGENTS.md との整合性

- カテゴリの正本は `.wiki-categories.json` とする。
- `AGENTS.md` の Managed tree（`pages/.../` 列挙）は `.wiki-categories.json` から自動生成・更新する。
  - `pages/` 実ディレクトリ由来の「未定義カテゴリ」は `AGENTS.md` 管理ブロックには自動反映しない（正本を設定ファイルに限定するため）。
- `AGENTS.md` のカテゴリ列挙ブロック再生成トリガーは `.wiki-categories.json` 更新時のみとする。
  - Import 保存時（ページ本文更新のみ）には再生成しない。
- 再生成トリガー以外でも、Wiki 起動時に管理ブロックの存在・構文・内容整合（カテゴリ設定との差分）を検査し、乖離があれば「自動修正は行わず」警告表示する。
  - 乖離判定は「カテゴリ集合の差分（不足・過剰・重複）」を対象とし、順序差異のみは警告対象外とする。
  - 比較は論理名（`ToLowerInvariant()`）で行い、大文字小文字差のみは同一カテゴリとして扱う。
  - 警告 UI には、利用者の明示操作で管理ブロックのみを再生成する `Repair` 導線を設ける（自動実行はしない）。
- 手編集保護のため、`AGENTS.md` に以下の管理ブロックを設け、その範囲のみ機械更新する。

```md
<!-- CURIA:CATEGORIES:BEGIN -->
- `pages/sources/`
- `pages/entities/`
- `pages/concepts/`
- `pages/analysis/`
<!-- CURIA:CATEGORIES:END -->
```

- 管理ブロックが無い既存ファイルは、初回更新時に末尾へ追記して移行する。
- BEGIN/END の多重出現・片側欠損・順序不正を検出した場合は自動更新を中止し、`AGENTS.md` 手動修復ガイドを UI に表示する（誤書き換え防止）。

### 3.2 要件B: プロンプト調整機能

#### B-1. 設定ファイル

`wiki/<domain>/.wiki-prompts.json` を追加する。

```json
{
  "version": 1,
  "import": {
    "systemPrefix": "",
    "systemSuffix": "",
    "userSuffix": ""
  },
  "query": {
    "systemPrefix": "",
    "systemSuffix": "",
    "userSuffix": ""
  },
  "lint": {
    "systemPrefix": "",
    "systemSuffix": "",
    "userSuffix": ""
  }
}
```

- 未存在時は空テンプレート生成。
- 保存時は原子的置換を必須とする（`<name>.tmp-<GUID>` へ書込 -> flush -> `File.Replace`/`File.Move` で差し替え）。
- 各値の上限は 8,000 文字（UTF-16 code unit 数）とする。
- 上限超過時は保存拒否（自動切り詰めは行わない）とし、超過項目名と文字数をエラーメッセージに含める。
- 保存時に機能別（Import/Query/Lint）で概算トークン数を算出し、モデル上限に対して高リスク（例: 80%以上）が見込まれる場合は警告を表示する（警告は保存拒否条件ではない）。
  - モデル未設定・未知モデル・上限取得失敗時は概算トークン警告を「未評価」として扱い、保存は許可する。
  - 未評価時は UI に「モデル上限を解決できないためリスク評価未実施」と明示する。
- 実行時にモデル上限（context length）超過が発生した場合は、API エラーを握りつぶさず UI に「どの機能（Import/Query/Lint）で失敗したか」を表示する。
- 上限超過の再発防止として、エラーメッセージには「Prompt Settings の Prefix/Suffix 短縮」を案内する。
- `.wiki-prompts.json.version` は将来拡張のための互換管理キーとする。
  - `version=1` のみ読込対象とする。
  - 未知バージョン（`version!=1`）は自動上書きせず、Prompt Settings を読み取り専用化する。
    - 既定プロンプトへ安全フォールバックし、ページ閲覧/手動保存など非LLM操作は継続可能とする。
    - Import/Query/Lint は実行不可にし、移行未対応である旨を UI に表示する。

#### B-2. プロンプト合成ルール

各機能の既存プロンプトをベースに、次順で合成する。

1. （`LlmClientService`）`LlmUserProfile` 注入（現行仕様どおり先頭）
2. `systemPrefix`
3. 既存実装のシステムプロンプト本体
4. `systemSuffix`

ユーザープロンプトは末尾に `userSuffix` を付与する。

- 既存ロジック（言語指定、JSON出力制約、参照形式）は保持する。
- `LlmUserProfile` 注入は現行どおり `LlmClientService` 側で維持。
- Import は複数 LLM 呼び出しそれぞれに同一ルールを適用する。
  - `GenerateIngestProposal`（候補選定）
  - `ApplyIngestResult` 内の最終反映プロンプト生成

#### B-3. UI

Wiki タブに「Prompt Settings」導線を追加し、ドメイン単位で編集できるようにする。

- Import / Query / Lint の3タブ
- Prefix / Suffix 編集
- 保存前バリデーション
- 「デフォルトへ戻す」ボタン
- `AiEnabled=false` でも Prompt Settings の閲覧/編集は可能とする（設定準備を許可）。
- `AiEnabled=false` の間は Import/Query/Lint 実行を不可とし、実行ボタン付近に無効理由を表示する。

---

## 4. スキーマファイル方針（AGENTS.md/CLAUDE.md）

現状実装は `wiki-schema.md` 利用であり、AGENTS 連携は未導入。

本更新では段階導入とする。

- v1（今回）: `wiki-schema.md` を正として維持。カテゴリ拡張とプロンプト調整を先行実装。
- v2（将来）: 読み込み優先順位 `AGENTS.md > CLAUDE.md > wiki-schema.md > fallback` を導入。

理由:

- 既存 Wiki 実装との互換を壊さず、先にユーザー要求（カテゴリ・プロンプト調整）を提供するため。
- ただし v1 でもカテゴリ列挙に関しては `AGENTS.md` を自動同期し、運用ドキュメントの乖離を防ぐ。

---

## 5. 実装タスク

### 5.1 C# 実装

1. `Models` に設定モデル追加
- `WikiCategoryConfig`
- `WikiPromptConfig`

2. `WikiService` 拡張
- `.wiki-categories.json` 読み書き
- `.wiki-prompts.json` 読み書き
- `GetCategoryFromPath` を動的判定へ変更（`pages/<first-segment>/`）
- `AGENTS.md` のカテゴリ管理ブロック再生成（カテゴリ設定変更時）
- `AGENTS.md` 管理ブロックの起動時整合チェック（乖離警告のみ）
- Wiki ドメイン単位のプロセス間排他ユーティリティ（名前付き `Mutex` もしくは同等）

3. `WikiViewModel` 修正
- `BuildPageTree` の固定カテゴリ配列廃止
- カテゴリ定義 + 実ディレクトリスキャンの統合表示

4. `WikiIngestService` 修正
- `ApplyIngestResult` で path バリデーション追加
- Import プロンプトに Prefix/Suffix 合成を適用
- 保存トランザクション（`.wiki-txn.json`）と起動時リカバリ処理を追加
- `phase=committed` 到達後の `.wiki-txn.json` 後片付け（削除再試行を含む）

5. `WikiQueryService` / `WikiLintService` 修正
- Query/Lint プロンプトに Prefix/Suffix 合成を適用

6. `Views/Pages/WikiPage.xaml(.cs)` / `WikiViewModel` に設定UI追加
- Prompt Settings 編集ダイアログ（または右ペイン）実装
- `AiEnabled` 状態に応じた実行ボタン有効/無効表示

### 5.2 ドキュメント更新

1. `docs/wiki-features-ja.md`
- 固定4カテゴリ記述を「デフォルト4カテゴリ + 拡張可」に更新
- プロンプト調整機能を追記

2. 運用ガイド
- `.wiki-categories.json` / `.wiki-prompts.json` の編集ルールを追記
- カテゴリの小文字正規化・大文字小文字非区別ルールを追記
- リネーム時のディレクトリ移動と失敗時ロールバック方針を追記

---

## 6. 受け入れ基準

### 6.0 優先度定義

- 受け入れ基準は `AC-<番号>` として扱う（例: 1番目は `AC-1`）。
- リリース判定は以下の優先度で行う。
  - `Must`: `AC-1,3,6,9,14,19,24,28,29,30,34,38,39,42,43,47,50,51,52,53,54,55`
  - `Should`: 上記以外
- `Must` 未達が1件でもある場合は出荷不可とする。
- AC 追加時の運用ルール:
  - 本書では、優先度の正本を本節の `Must` 列挙とする（各 AC 本文は機能要件記述を主目的とし、優先度の正本とはしない）。
  - 新規 AC を追加する場合、同一コミットで必ず本節の `Must` 列挙を更新する（新規 AC が `Should` の場合は `Must` 列挙更新不要）。
  - 優先度変更（`Should -> Must` / `Must -> Should`）時も、本節の `Must` 列挙を正として更新する。
- 保守性向上のため、受け入れ基準の機械可読台帳（例: `docs/wiki-ac-priority.json`）を別途持つことを推奨する。
  - 導入時は同ファイルを優先度参照の正本とし、本節は人間可読ビューとして同期更新する。
- 本版で追加する `AC-56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76` の優先度は `Should` とする。

1. `pages/tables/` を作成すると Pages タブに表示される。
2. `.wiki-categories.json` に `tables` を追加した状態で Import が `pages/tables/x.md` を返した場合、保存成功する。
3. Import が `../evil.md` や `raw/x.md` を返した場合、保存拒否される。
4. `.wiki-prompts.json` の `query.systemPrefix` を変更すると、次回 Query 実行に即時反映される。
5. Prompt 設定を空にすると、現行同等のプロンプト動作に戻る。
6. 既存プロジェクト（設定ファイルなし）でも従来どおり動作する。
7. カテゴリ追加後、`AGENTS.md` の管理ブロックに新カテゴリが反映される。
8. `AGENTS.md` の管理ブロック外の手編集内容は維持される。
9. Import が `pages/../../raw/x.md` を返した場合、正規化後の配下外判定で保存拒否される。
10. `Tables` と `tables` を同時登録しようとすると、重複（大文字小文字非区別）として拒否される。
11. `sources` の削除またはリネームは拒否される。
12. カテゴリに `CON`、`bad.`、`bad `、`a/b`、`a:b` を指定した場合、すべて保存拒否される。
13. カテゴリリネーム時、`pages/<old>/` から `pages/<new>/` へ移動成功した場合のみ設定が確定する（失敗時はロールバック）。
14. Import 保存は I/O エラー時に `.wiki-txn.json` を使ったロールバックを試行し、成功時は部分反映を残さない。
15. `query.systemPrefix` に 8,001 文字を設定した場合、保存拒否される（項目名と文字数を表示）。
16. Import の候補選定・最終反映の両方で、同じ Prefix/Suffix 合成ルールが適用される。
17. `AGENTS.md` の管理ブロック更新はカテゴリ設定更新時のみ発生し、Import 保存では発生しない。
18. 既存 `AGENTS.md` に管理ブロックがない場合、初回カテゴリ更新時に管理ブロックが末尾追加される。
19. Import の `newPages` / `updatedPages` に不正 path が1件でも含まれる場合、全保存処理が中止され、他の正当ページも保存されない。
20. Import が `pages2/x.md`、`C:\tmp\x.md`、`\\server\\share\\x.md`、`pages/tables/a/b.md` を返した場合、すべて保存拒否される。
21. Import が `pages\\tables\\x.md` を返した場合、区切り正規化後に `pages/tables/x.md` として検証される。
22. `pagesRoot` 配下判定は `OrdinalIgnoreCase` + 末尾セパレータ境界で行われ、prefix 偽装（`pages2`）を許容しない。
23. カテゴリ削除後に `pages/<category>/` が残っている場合、Pages タブには未定義カテゴリとして表示されるが、Import 保存先としては拒否される（仕様どおり）。
24. `AiEnabled=false` 時、Prompt Settings は編集できるが Import/Query/Lint 実行は拒否される。
25. `Tables` のような既存大文字混在カテゴリは論理名 `tables` として扱われ、表示・重複判定は小文字正規化基準で行われる。
26. カテゴリリネーム中断で `*.rename-tmp-*` が残っていた場合、次回起動時に自動復旧が試行される。
27. Prompt が原因で context length 超過が発生した場合、失敗機能名と短縮案内付きでエラー表示される。
28. Import 途中失敗後に `.wiki-txn.json` が残っていた場合、次回起動時に整合回復処理が実行される。

---

## 7. 補足（現状との差分サマリ）

- AGENTS.md/CLAUDE.md は「生成済みだが未使用」。
- カテゴリは「保存は柔軟、表示と分類は固定」。
- プロンプトは「グローバル調整のみ、Wiki機能別の調整なし」。

上記ギャップを埋める最小差分として、今回の To-Be を定義した。

---

## 8. レビュー反映追記（欠陥対策の明文化）

### 8.1 Import トランザクション復旧の状態機械を明示

- `.wiki-txn.json` には少なくとも以下を保持する。
  - `transactionId`
  - `phase`（`prepared` / `committing` / `committed` / `rollbacking` / `rolled_back`）
  - `createdAtUtc`
  - `entries[]`（`entryType=create|update`, `targetPath`, `tempPath`, `backupPath`, `state`）
- `entries[].targetPath` は正規化後に一意でなければならない。重複検出時は `phase=prepared` から先へ遷移せず、保存処理を中止する。
- `entryType` は復旧判定の正本として扱う。
  - `create`: 旧ファイル非存在を前提とし、ロールバック時は `targetPath` を削除して復元する。
  - `update`: 旧ファイル存在を前提とし、ロールバック時は `backupPath` から復元する。
  - `entryType=create` で `backupPath` が存在する、または `entryType=update` で `backupPath` 欠損の場合は不整合として再ロールバック優先で扱う。
- `entries[].state` は以下の列挙値のみ許可する。
  - `prepared`（計画登録済み、未反映）
  - `temp_written`（一時ファイル書込・flush 済み）
  - `replaced`（対象へ反映済み）
  - `rolled_back`（対象の旧状態復元済み）
- `entries[].state` は単調前進のみ許可する。
  - `prepared -> temp_written -> replaced`
  - `prepared|temp_written|replaced -> rolled_back`
- 実ファイルへの最初の反映（`File.Replace` / `File.Move`）を行う前に、必ず `phase=committing` を永続化する。
- `phase` 遷移は単調前進のみ許可し、起動時復旧は `phase` と `entries[].state` に基づいて「再コミット」または「再ロールバック」を一意に決定する。
  - `phase=committing`: `replaced` 以外が1件でもあれば再ロールバック、全件 `replaced` なら再コミット（後片付け）
  - `phase=rollbacking`: 未 `rolled_back` のエントリに対して再ロールバック継続
  - `phase=prepared`: `entries[].state` が全件 `prepared` の場合のみジャーナル削除。`temp_written`/`replaced` が1件でもあれば異常状態として再ロールバックを優先する。
- 判定不能状態（JSON破損・欠損項目）は「再ロールバック優先」で扱い、失敗時は手動復旧手順を表示する。
- `phase=committed` 到達後は `.wiki-txn.json` を削除し、残存検出時は内容を再検証したうえで「コミット済みの後片付け」として削除のみ再試行する（再コミット/再ロールバックは行わない）。

### 8.2 書き込み経路の共通パスバリデーション

- `pages/<category>/<name>.md` のパス検証は Import 専用ではなく、Wiki ページを書き込む全経路に共通適用する。
  - 対象: Import 保存、手動保存（`SavePage`）、将来追加される自動更新機能。
- 実装は `WikiService` などの共通ユーティリティに集約し、経路ごとの実装差異を禁止する。
- UI 挙動も共通化し、未定義カテゴリ選択時は全経路で保存ボタン無効化 + 同一理由文言表示を行う。

### 8.3 ページ名（`<name>`）禁止条件の統一

- 本項の内容は A-3 の `<name>` 禁止条件へ統合済みとし、全保存経路で同一ルールを適用する。

### 8.4 設定ファイル破損時の復旧方針

- `.wiki-categories.json` / `.wiki-prompts.json` の読込失敗は「不正JSON」と「I/O例外」を分離して扱う。
  - 不正JSON（パース不能）の場合:
    1. 破損ファイルを `<name>.broken-<yyyyMMdd-HHmmss-fff>-<GUID>.json` へ退避
    2. デフォルト設定を再生成
       - `.wiki-categories.json` の場合は初回生成と同じ規則で `pages/` 直下ディレクトリを再取り込みする（小文字正規化 + 競合検出）。
       - 正規化名競合がある場合は自動取り込みを中止し、競合解消ガイドを表示したうえで読み取り専用モードへ遷移する。
    3. UI に「設定を復旧した」旨と退避先を通知
  - I/O例外（共有違反・一時ロック・アクセス拒否など）の場合:
    1. 短時間リトライ（例: 最大3回、指数バックオフ）を実施
    2. 回復しない場合は「設定破損扱い」にせず読み取り専用モードへ遷移
    3. UI に I/O 起因であることと再試行導線を表示
- 退避にも失敗した場合は読み取り専用モードで起動し、保存操作を拒否する。
- 読み取り専用の適用範囲は原因ファイルごとに分離する。
  - `.wiki-categories.json` 起因: Wiki ドメイン全体を読み取り専用化する。
  - `.wiki-prompts.json` 起因: Prompt Settings のみ読み取り専用化し、Import/Query/Lint を無効化する（非LLM操作は継続）。

### 8.5 未定義カテゴリ表示の UX ルール

- `pages/` 実ディレクトリ由来の未定義カテゴリは Pages タブで表示する際、`read-only` 表示を付与する。
- 未定義カテゴリ選択時、保存不可理由（`.wiki-categories.json` 未定義）と「カテゴリ定義へ追加」導線を表示する。
- Import 保存拒否時も同一理由文言を使用し、UI とログの説明を統一する。
- Editor/SavePage など別導線から到達した場合も同一理由文言・同一操作制限（保存不可）を適用する。
- UI 表示文言はプロジェクト規約に合わせて英語を正とし、本書中の日本語文言は仕様説明用の例示として扱う。

### 8.6 同時実行制御

- Wiki ドメイン単位で排他制御を導入する。
  - 対象操作: カテゴリ追加/削除/リネーム、Import 保存、起動時トランザクション復旧、`AGENTS.md` 管理ブロック更新。
- 排他は同一プロセス内だけでなく複数 Curia プロセス間でも有効とする（ドメイン単位の名前付き `Mutex` または同等のファイルロックを使用）。
- 名前付き `Mutex` 利用時に `AbandonedMutexException` が発生した場合は「排他取得成功 + 前回異常終了の可能性あり」として扱い、復旧処理を継続する。
- 同時実行時は先行処理を優先し、後続処理は待機または明示エラー（タイムアウト付き）とする。
- 排他獲得失敗時のユーザー通知文言を統一する（例: 「別の Wiki 更新処理が実行中です」）。
- 排他待機の既定値は以下とする。
  - 待機タイムアウト: 30 秒
  - 自動再試行: 1 回（再試行時も 30 秒）
  - 待機中はキャンセル可能な進捗 UI を表示する。
  - 2 回とも失敗時は統一文言でエラー通知し、ユーザー操作でのリトライのみ許可する。

### 8.7 追加受け入れ基準

29. `.wiki-txn.json` の `phase=committing` で異常終了した後の再起動時、仕様化された状態機械に従って整合回復（再コミットまたは再ロールバック）が一意に選択される。  
30. `SavePage` 経由でも `raw/x.md` や `../x.md` は拒否され、Import と同じ検証ルールが適用される。  
31. Import が `pages/tables/bad.` または `pages/tables/bad ` を返した場合、`<name>` 末尾規則違反で保存拒否される。  
32. `.wiki-categories.json` が不正JSONの場合、`.broken-<timestamp>` へ退避後にデフォルト再生成され、UI に復旧通知が表示される。  
33. 未定義カテゴリは Pages タブに `read-only` として表示され、保存不可理由と「カテゴリ定義へ追加」導線が表示される。  
34. カテゴリリネームと Import 保存を同時実行した場合、ドメイン排他により片方が待機または明示エラーになり、競合更新が発生しない。  
35. `AGENTS.md` 管理ブロック更新とカテゴリ設定更新の同時実行でも、最終状態が単一の確定順序に収束する（部分書き換えが残らない）。  
36. 排他獲得失敗時は統一文言で UI 通知され、リトライ可能である。  
37. カテゴリリネームで「ディレクトリ移動成功後に `.wiki-categories.json` 更新失敗」が起きた場合、`pages/<new>/ -> pages/<old>/` の補償移動が実行され、設定と実体が旧状態に戻る。  
38. Import で `indexUpdate` / `logEntry` を含む更新中に I/O エラーが発生した場合、`pages/`・`index.md`・`log.md` を含む全対象が `.wiki-txn.json` に基づいて整合回復される。  
39. リネーム中断で `.wiki-rename-txn.json` と `*.rename-tmp-*` が残った再起動時、`phase` に基づく一意な復旧経路が選択される。  
40. `AGENTS.md` のカテゴリ管理ブロックが多重定義または片側欠損の場合、自動更新は中止され、手動修復ガイドが表示される。  
41. 設定破損ファイルの連続復旧（同秒内複数回）でも、退避ファイル名衝突が発生しない。  
42. Import 正常完了時は `.wiki-txn.json` が削除され、次回起動時に未完了トランザクションとして扱われない。  
43. 同一ドメインを2プロセスで同時更新しようとした場合、ドメイン排他（プロセス間ロック）により片方が待機またはタイムアウトエラーになる。  
44. `AGENTS.md` の管理ブロック内容がカテゴリ設定と乖離している場合、起動時整合チェックで警告が表示される（自動書き換えは行わない）。  
45. `.wiki-prompts.json` が不正JSONの場合、`.broken-<timestamp>` へ退避後にデフォルト再生成され、UI に復旧通知が表示される。  
46. Import が `pages/Tables/x.md` を返した場合、カテゴリ照合は小文字正規化で判定され、`tables` 定義済みなら保存成功する。  
47. `pages/<category>/` への保存経路上に junction/symlink が存在する場合、配下外書き込みリスクとして保存拒否される。  
48. `newPages` のみを含む Import でも保存成功し、ロールバック時は新規作成ファイル削除で旧状態へ戻る。  
49. `pages/<old>/` が存在しないカテゴリをリネームした場合、カテゴリ設定のみ更新され、`pages/<new>/` は自動作成されない。  
50. 未定義カテゴリへ Editor/SavePage 導線で保存しようとした場合も、Import と同一理由文言で保存拒否される。  
51. `.wiki-categories.json` 初回生成時に `pages/` 直下の既存ディレクトリ自動取り込みで正規化名競合が検出された場合、自動取り込みは中止され、競合解消ガイド表示付きで読み取り専用モード起動となる。  
52. 読み取り専用モードに入った場合、原因解消後の「再読み込み」またはアプリ再起動で解除判定が再実行され、解除条件を満たせば通常モードへ復帰する。  
53. 排他制御の待機は 30 秒 + 1 回再試行で打ち切られ、待機中 UI はキャンセル可能である。  
54. `wiki/<domain>/` より上位階層の reparse point は保存拒否理由にせず、`wiki/<domain>/pages/` 以降のみを判定対象にする。  
55. カテゴリ名は保存後に UI 表示も小文字へ統一され、表示名のみ元入力を保持しない。  
56. カテゴリリネーム中断時に `.wiki-rename-txn.json.phase=prepared` が残っていた場合、起動時復旧は `old` 維持を基準に一意な経路で収束し、ジャーナルと一時ディレクトリが後片付けされる。  
57. `.wiki-txn.json` の各 `entry` に `entryType` が記録され、`create` は削除ロールバック、`update` はバックアップ復元ロールバックが適用される。  
58. `.wiki-categories.json` 通常保存で異常終了しても、正本として空ファイルや途中書き込み JSON が残らず、再起動後は直前の有効内容を読み出せる。  
59. `.wiki-prompts.json` 通常保存で `File.Replace`/`File.Move` が失敗した場合、旧ファイル内容が維持され、保存失敗通知が UI に表示される。  
60. Import 保存対象（`newPages` / `updatedPages` / `indexUpdate` / `logEntry`）に正規化後同一 `targetPath` が含まれる場合、保存処理は開始されず重複エラーが表示される。  
61. `.wiki-categories.json` が不正JSONから復旧される際、初回生成と同じ規則で `pages/` 直下カテゴリが再取り込みされる（競合時は読み取り専用モードへ遷移する）。  
62. Prompt Settings 保存時、概算トークンが高リスク閾値（例: 80%）を超える場合は警告が表示されるが、保存自体は拒否されない。  
63. プロセス間排他で `AbandonedMutexException` が発生した場合、処理は中断せず「ロック取得成功」として復旧フローへ進む。  
64. 受け入れ基準の優先度は 6.0 節を正本として管理され、機械可読台帳を導入した場合は同台帳を正本として 6.0 節と同期される。AC 追加・優先度変更時は同一コミットで両者が更新される。  
65. Import 保存対象の `targetPath` 重複判定は `Path.GetFullPath` + `OrdinalIgnoreCase` で行われ、`pages/tables/x.md` と `pages/Tables/x.md` は重複として扱われる。  
66. `AiEnabled=false` かつモデル未設定の状態でも Prompt Settings 保存は可能で、概算トークン警告は「未評価」として表示される。  
67. `.wiki-categories.json` の `version!=1` を検出した場合、自動上書きせず Wiki ドメイン全体を読み取り専用モードで起動し、移行未対応通知が表示される。  
68. `pages/<old>/` が存在しないカテゴリリネームで設定更新に失敗した場合、ファイル移動なしで旧設定に戻り、部分適用が残らない。  
69. `AGENTS.md` 管理ブロック乖離警告では、自動修正は行わず、利用者が明示実行する `Repair` 導線が表示される。  
70. `.wiki-prompts.json` の `lint.systemSuffix` を変更すると、次回 Lint 実行に即時反映される。  
71. Query/Lint 実行で context length 超過が発生した場合、失敗機能名（Query または Lint）と Prompt Settings 短縮案内が UI に表示される。  
72. `AiEnabled=false` 時、Query/Lint の実行ボタンは無効化され、Import と同一ポリシーで無効理由が表示される。  
73. `.wiki-prompts.json` の `version!=1` を検出した場合、Prompt Settings は読み取り専用になり、Import/Query/Lint は無効化される一方で、ページ閲覧/手動保存など非LLM操作は継続できる。  
74. Import が `pages/tables/X.MD` を返した場合、拡張子の大文字小文字差は許容され、保存時に `.md` 正規化で扱われる。  
75. `entryType=create` のロールバック時に `targetPath` が外部変更されていた場合、自動削除は行わず隔離/手動確認導線へ遷移する。  
76. リネーム復旧で `new -> old` または `temp -> old` 実行時に `oldPath` 既存衝突がある場合、上書きせず衝突退避手順を経由し、失敗時は手動復旧案内が表示される。  

### 8.8 設定ファイル通常保存時の原子性

- `.wiki-categories.json` / `.wiki-prompts.json` は通常保存時も「一時ファイル書込 + flush + 原子的差し替え」を必須とする。
- 保存途中の異常終了時に「空ファイル」「途中書込みJSON」が正本として残らないことを保証する。
- 置換失敗時は旧ファイルを維持し、UI に保存失敗を通知する（自動再試行は1回まで）。

### 8.9 読み取り専用モードの復帰条件

- 読み取り専用モードのトリガー原因（例: 設定競合、退避失敗、`version!=1`）を内部状態として保持し、UI に明示する。
- 復帰判定は次のタイミングで実行する。
  1. ユーザーの「再読み込み」操作
  2. Wiki タブ再初期化
  3. アプリ再起動
- 復帰条件:
  - 競合起因: 正規化名競合が解消されていること
  - 退避失敗起因: 退避先への書き込み可能性が回復していること
- 復帰成功時は読み取り専用フラグを解除し、保存系 UI を再有効化する。
- 復帰失敗時は読み取り専用を維持し、再試行可能な同一導線を残す。

---

## 9. 既存Wikiへの影響範囲

### 9.1 自動移行されるもの

- `.wiki-categories.json` / `.wiki-prompts.json` が無い既存Wikiは、初回アクセス時に自動生成される。
- `.wiki-categories.json` 初回生成時、`pages/` 直下の既存カテゴリディレクトリは小文字正規化のうえ自動取り込みされる（競合時を除く）。
- 既存の `pages/Tables/` のような大文字混在ディレクトリは、論理名小文字で継続利用できる（即時リネーム不要）。
- 既存 `AGENTS.md` に管理ブロックが無い場合、初回カテゴリ更新時に末尾追加される。

### 9.2 仕様変更で挙動が変わるもの

- `.wiki-categories.json` 未定義カテゴリへの保存（Import/SavePage）は拒否される。従来「保存できていた」運用は、カテゴリ定義追加が必要になる。
- Import の path 制約が厳格化され、`pages/` 配下外・多段パス・予約名などは保存不可になる。
- 保存/リネーム中に残ったトランザクションファイル（`.wiki-txn.json`, `.wiki-rename-txn.json`）は、起動時に復旧処理が走る。

### 9.3 事前確認を推奨する項目

- `pages/` 配下に「今後も更新したいカテゴリ」があれば、先に `.wiki-categories.json` へ登録する。
- 既存運用で junction/symlink を使っている場合、保存拒否対象になるため実体配置へ移行する。
- `AGENTS.md` の管理ブロックが壊れている場合は、自動更新されず警告のみになるため手動修復する。
