# xbrlplus

EDINETのXBRL文書（スキーマ・インスタンス）を解析し、SQLiteデータベースとして対話的に操作できるREPLツールです。  
Interactive REPL tool for parsing EDINET XBRL schemas and instances into queryable SQLite format.

## Features

- Parses `.xsd` and `.xbrl` files
- Stores parsed data in in-memory SQLite
- Interactive REPL with `.format`, `.output`, `.exit` commands

## Installation

To install `xbrlplus` as a global .NET CLI tool, run the following command:

```bash
dotnet tool install --global xbrlplus
```

To update to the latest version:
```bash
dotnet tool update --global xbrlplus
```

To uninstall:
```bash
dotnet tool uninstall --global xbrlplus
```

🧭 Note: Make sure you have the .NET SDK installed and available in your PATH.

## Usage

```bash
xbrlplus.exe <schema_or_instance_file>
```

## REPL Commands
- `.format [table|csv|json]` — Set output format
- `.output <filename>` — Redirect output to file
- `.exit` / `.quit` / `.q` — Exit REPL

## Custom SQLite Functions

This project includes custom SQLite functions to support flexible querying, text normalization, and other domain-specific operations. These functions are designed to be composable and safe for mixed-format data, and may evolve as the project grows.

### `MATCHES_REGEX(text, pattern)`
Returns `true` if the input string matches the given regular expression pattern.

- **Arguments**:
  - `text`: The input string to test.
  - `pattern`: A regular expression pattern (ECMAScript-compatible).
  - `caseSensitive` *(optional)*: A boolean flag indicating whether the match should be case-sensitive. Defaults to `true`.
- **Returns**: `true` or `false`
- **Behavior**:
  - Returns `false` if either argument is `NULL`.
  - Throws an error if the pattern is syntactically invalid.
  - Case sensitivity is controlled by an optional third argument (`true` by default).

**Example**:
```sql
SELECT * FROM TConcepts WHERE MATCHES_REGEX(LocalName, '^Cash.*');
```

### `CLEAN_HTML_TEXT(html_or_text)`

Extracts meaningful plain text from an HTML (or XHTML) snippet, normalizing whitespace and preserving semantic spacing.

- **Arguments**:
  - `html_or_text`: A string containing either raw text or HTML markup.
- **Returns**: A whitespace-normalized plain text string.
- **Behavior**:
  - Removes all HTML tags.
  - Replaces `<br>` tags with a single space.
  - Appends a space after block-level tags (e.g., `div`, `p`, `section`, `article`).
  - Converts `&nbsp;`, tabs, newlines, and multiple ASCII spaces into a single ASCII space.
  - Preserves full-width (U+3000) spaces.
  - Trims leading and trailing whitespace.
  - Applies normalization even if the input contains no HTML.

**Example**:
```sql
SELECT CLEAN_HTML_TEXT(Value) AS CleanedText FROM VFacts;
```

### `EXTRACT_URI_TAIL(uri)`

Extracts the final segment from a URI-like string, assuming segments are separated by `/`.

- **Arguments**:
  - `uri`: A string representing a URI or path.
- **Returns**: The last non-empty segment of the URI.
- **Behavior**:
  - Trims trailing slashes before splitting.
  - Returns an empty string if input is `NULL`, empty, or malformed.
  - Designed to be fault-tolerant and safe for irregular input.

**Example**:

```sql
SELECT EXTRACT_URI_TAIL(Uri) AS FileName from TDocuments;
```

## Table Layout

### TDocuments（文書マスタ）

| Column | Type    | Description                     |
|--------|---------|---------------------------------|
| Id     | INTEGER | 主キー。文書の一意識別子       |
| Kind   | TEXT    | 文書種別（`TaxonomySchema` / `Instance` / `Linkbase`）  |
| Uri    | TEXT    | 取得元URI         |

---

### TDocumentNodes（木構造ノード）

| Column     | Type    | Description                             |
|------------|---------|-----------------------------------------|
| Id         | INTEGER | 主キー。ノードの一意識識別子           |
| DocumentId | INTEGER | 外部キー。`TDocuments.Id` を参照       |
| Depth      | INTEGER | 階層レベル（ルートが0）                 |
| ParentId   | INTEGER | 親ノードID（nullならルート）           |

---

### VDocumentNodes（ビュー：構造と文書の結合）

| Column        | Type    | Description                             |
|---------------|---------|-----------------------------------------|
| DocumentNodeId| INTEGER | ノードID（`TDocumentNodes.Id`）         |
| ParentId      | INTEGER | 親ノードID                              |
| Depth         | INTEGER | 階層レベル                              |
| DocumentId    | INTEGER | 文書ID（`TDocuments.Id`）               |
| Kind          | TEXT    | 文書種別（schema / instance）           |
| Uri           | TEXT    | 文書の取得元URI                         |

---

### TConcepts（概念定義）

| Column         | Type    | Description                                      |
|----------------|---------|--------------------------------------------------|
| Id             | INTEGER | 主キー。概念の一意識別子                        |
| Uri            | TEXT    | 概念のURI（XBRL定義）                            |
| NamespaceName  | TEXT    | 名前空間URI                                      |
| LocalName      | TEXT    | ローカル名（タグ名）                             |
| TypeNS         | TEXT    | データ型の名前空間URI（例: `http://www.xbrl.org/...`） |
| TypeName       | TEXT    | データ型のローカル名（例: `stringItemType`, `monetaryItemType` など） |
| Balance        | TEXT    | 借方/貸方（`Debit` / `Credit` / `Undefined`）                  |
| Abstract       | TEXT    | 抽象概念かどうか（`true` / `false`）             |
| PeriodType     | TEXT    | 期間型（`duration` / `instant`）                 |
| Nillable       | TEXT    | null許容かどうか（`true` / `false`）             |

---

### TContexts（報告期間・時点）

| Column     | Type    | Description                                      |
|------------|---------|--------------------------------------------------|
| Id         | INTEGER | 主キー。コンテキストの一意識別子                |
| Name       | TEXT    | コンテキスト名（例：CurrentYearDuration）        |
| StartDate  | TEXT    | 開始日（nullable）                               |
| EndDate    | TEXT    | 終了日（nullable）                               |
| Instant    | TEXT    | 時点（nullable）                                 |

---

### TContextSenarios（コンテキストの次元構成）

| Column      | Type    | Description                                      |
|-------------|---------|--------------------------------------------------|
| Id          | INTEGER | 主キー。構成の一意識別子                        |
| ContextId   | INTEGER | 外部キー。`TContexts.Id` を参照                 |
| DimensionId | INTEGER | 次元の概念ID（`TConcepts.Id`）                  |
| MemberId    | INTEGER | メンバーの概念ID（`TConcepts.Id`）              |

---

### VContextSenarios（ビュー：コンテキストと次元の結合）

| Column         | Type    | Description                                      |
|----------------|---------|--------------------------------------------------|
| ContextSenarioId | INTEGER | シナリオID（`TContextSenarios.Id`）           |
| ContextName    | TEXT    | コンテキスト名（`TContexts.Name`）              |
| DimensionNS    | TEXT    | 次元の名前空間（`TConcepts.NamespaceName`）     |
| DimensionName  | TEXT    | 次元のローカル名（`TConcepts.LocalName`）       |
| MemberNS       | TEXT    | メンバーの名前空間（`TConcepts.NamespaceName`） |
| MemberName     | TEXT    | メンバーのローカル名（`TConcepts.LocalName`）   |

---

### VContextDetails（ビュー：コンテキスト詳細＋次元構成）

| Column         | Type    | Description                                      |
|----------------|---------|--------------------------------------------------|
| ContextId      | INTEGER | コンテキストID（`TContexts.Id`）                |
| ContextName    | TEXT    | コンテキスト名（`TContexts.Name`）              |
| StartDate      | TEXT    | 開始日（nullable）                              |
| EndDate        | TEXT    | 終了日（nullable）                              |
| Instant        | TEXT    | 時点（nullable）                                |
| ContextSenarioId | INTEGER | シナリオID（nullable）                        |
| DimensionNS    | TEXT    | 次元の名前空間（nullable）                      |
| DimensionName  | TEXT    | 次元のローカル名（nullable）                    |
| MemberNS       | TEXT    | メンバーの名前空間（nullable）                  |
| MemberName     | TEXT    | メンバーのローカル名（nullable）                |

---

### TFacts（事実データ）

| Column     | Type    | Description                                      |
|------------|---------|--------------------------------------------------|
| Id         | INTEGER | 主キー。事実の一意識別子                        |
| ConceptId  | INTEGER | 外部キー。`TConcepts.Id` を参照                  |
| ContextId  | INTEGER | 外部キー。`TContexts.Id` を参照                  |
| Nil        | INTEGER | null指定（0: 値あり, 1: null）                   |
| Decimals   | INTEGER | 精度（小数点以下桁数。nullable）                |
| Unit       | TEXT    | 単位（例：JPY、shares。nullable）               |
| Value      | TEXT    | 値（文字列として格納。nullable）                |

---

### VFacts（ビュー：事実＋概念＋コンテキスト）

| Column        | Type    | Description                                      |
|---------------|---------|--------------------------------------------------|
| FactId        | INTEGER | 事実ID（`TFacts.Id`）                            |
| NamespaceName | TEXT    | 概念の名前空間（`TConcepts.NamespaceName`）     |
| LocalName     | TEXT    | 概念のローカル名（`TConcepts.LocalName`）       |
| Nil           | INTEGER | null指定（0: 値あり, 1: null）                   |
| Decimals      | INTEGER | 精度（nullable）                                |
| Unit          | TEXT    | 単位（nullable）                                |
| Value         | TEXT    | 値（nullable）                                  |
| ContextName   | TEXT    | コンテキスト名（`TContexts.Name`）              |
| StartDate     | TEXT    | 開始日（nullable）                              |
| EndDate       | TEXT    | 終了日（nullable）                              |
| Instant       | TEXT    | 時点（nullable）                                |

---

### TLabels（概念ラベル）

| Column    | Type    | Description                                      |
|-----------|---------|--------------------------------------------------|
| Id        | INTEGER | 主キー。ラベルの一意識別子                      |
| ConceptId | INTEGER | 外部キー。`TConcepts.Id` を参照                  |
| Lang      | TEXT    | 言語コード（例：ja、en）                         |
| Text      | TEXT    | ラベル本文                                       |
| Role      | TEXT    | ラベルの役割（例：label、terseLabel）           |

---

### VLabels（ビュー：ラベル＋概念）

| Column        | Type    | Description                                      |
|---------------|---------|--------------------------------------------------|
| LabelId       | INTEGER | ラベルID（`TLabels.Id`）                         |
| NamespaceName | TEXT    | 概念の名前空間（`TConcepts.NamespaceName`）     |
| LocalName     | TEXT    | 概念のローカル名（`TConcepts.LocalName`）       |
| Lang          | TEXT    | 言語コード                                       |
| Text          | TEXT    | ラベル本文                                       |
| Role          | TEXT    | ラベルの役割                                     |

---

### TReferences（概念の参照情報）

| Column            | Type    | Description                                      |
|-------------------|---------|--------------------------------------------------|
| Id                | INTEGER | 主キー。参照の一意識別子                        |
| ConceptId         | INTEGER | 外部キー。`TConcepts.Id` を参照                  |
| RefNamespaceName  | TEXT    | 参照の名前空間（http://www.xbrl.org/2006/ref ） |
| RefLocalName      | TEXT    | 参照のローカル名                               |
| RefValue          | TEXT    | 参照先（例：法令名、会計基準）                   |
| RefOrder          | INTEGER | 表示順序                                         |

---

### VReferences（ビュー：参照＋概念）

| Column            | Type    | Description                                      |
|-------------------|---------|--------------------------------------------------|
| ReferenceId       | INTEGER | 参照ID（`TReferences.Id`）                       |
| NamespaceName     | TEXT    | 概念の名前空間（`TConcepts.NamespaceName`）     |
| LocalName         | TEXT    | 概念のローカル名（`TConcepts.LocalName`）       |
| RefNamespaceName  | TEXT    | 参照の名前空間（http://www.xbrl.org/2006/ref ） |
| RefLocalName      | TEXT    | 参照のローカル名                               |
| RefValue          | TEXT    | 参照先（例：法令名、会計基準）                   |
| RefOrder          | INTEGER | 表示順序                                         |

---

### TRoleTypes（ロール定義）

| Column        | Type    | Description                                      |
|---------------|---------|--------------------------------------------------|
| Id            | INTEGER | 主キー。ロールの一意識別子                      |
| RoleURI       | TEXT    | ロールURI（XBRL定義）                            |
| Definition    | TEXT    | 日本語名（nullable）                         |
| DefinitionEn  | TEXT    | ジェネリックリンクで定義された英語名（nullable）                           |

---

### TLinkNodes（リンク構造ノード）

| Column          | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| Id              | INTEGER | 主キー。ノードの一意識識別子                    |
| RoleTypeId      | INTEGER | 外部キー。`TRoleTypes.Id` を参照                |
| LinkType        | TEXT    | リンク種別（例：PresentationLink）              |
| Depth           | INTEGER | 階層レベル（ルートが0）                         |
| Seq             | INTEGER | 同階層内での順序                                |
| ArcOrder        | REAL    | アーク順序（nullable）                          |
| PreferredLabel  | TEXT    | 優先ラベル（nullable）                          |
| Arcrole         | TEXT    | アークロール（nullable）                        |
| Weight          | INTEGER | 重み（nullable）                                |
| ParentId        | INTEGER | 親ノードID（nullable）                          |
| ConceptId       | INTEGER | 外部キー。`TConcepts.Id` を参照                 |

---

### VPresentationLinkNodes（ビュー：プレゼンテーションリンク構造）

| Column          | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| LinkNodeId      | INTEGER | ノードID（`TLinkNodes.Id`）                     |
| ParentId        | INTEGER | 親ノードID（nullable）                          |
| RoleURI         | TEXT    | ロールURI（`TRoleTypes.RoleURI`）              |
| Definition      | TEXT    | ロール定義（日本語）                            |
| DefinitionEn    | TEXT    | ロール定義（英語）                              |
| Depth           | INTEGER | 階層レベル                                      |
| Seq             | INTEGER | 同階層内での順序                                |
| ArcOrder        | REAL    | アーク順序（nullable）                          |
| PreferredLabel  | TEXT    | 優先ラベル（nullable）                          |
| ConceptId       | INTEGER | 概念ID（`TConcepts.Id`）                        |
| LocalName       | TEXT    | 概念のローカル名                                |

---

### VDefinitionLinkNodes（ビュー：定義リンク構造）

| Column          | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| LinkNodeId      | INTEGER | ノードID（`TLinkNodes.Id`）                     |
| ParentId        | INTEGER | 親ノードID（nullable）                          |
| RoleURI         | TEXT    | ロールURI（`TRoleTypes.RoleURI`）              |
| Definition      | TEXT    | ロール定義（日本語）                            |
| DefinitionEn    | TEXT    | ロール定義（英語）                              |
| Depth           | INTEGER | 階層レベル                                      |
| Seq             | INTEGER | 同階層内での順序                                |
| ArcOrder        | REAL    | アーク順序（nullable）                          |
| Arcrole         | TEXT    | アークロール（nullable）                        |
| ConceptId       | INTEGER | 概念ID（`TConcepts.Id`）                        |
| LocalName       | TEXT    | 概念のローカル名                                |

---

### VCalclationLinkNodes（ビュー：計算リンク構造）

| Column          | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| LinkNodeId      | INTEGER | ノードID（`TLinkNodes.Id`）                     |
| ParentId        | INTEGER | 親ノードID（nullable）                          |
| RoleURI         | TEXT    | ロールURI（`TRoleTypes.RoleURI`）              |
| Definition      | TEXT    | ロール定義（日本語）                            |
| DefinitionEn    | TEXT    | ロール定義（英語）                              |
| Depth           | INTEGER | 階層レベル                                      |
| Seq             | INTEGER | 同階層内での順序                                |
| ArcOrder        | REAL    | アーク順序（nullable）                          |
| Weight          | INTEGER | 計算重み（nullable）                            |
| ConceptId       | INTEGER | 概念ID（`TConcepts.Id`）                        |
| LocalName       | TEXT    | 概念のローカル名                                |