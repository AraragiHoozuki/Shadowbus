# Shadowbus 一体化配置编辑器

这是一个纯前端 React 应用，用表单编辑 Shadowbus 的 `AIData`、`BossRush`、
`CardMaster`、`Format` 和 `TwoPick` 配置。应用可以部署到 GitHub Pages，所有文件
都只在浏览器本地处理，不会上传到服务器。

## 使用

推荐使用最新版 Edge 或 Chrome，点击“打开 Mods 目录”，选择游戏根目录下的
`Mods`。首次写入时浏览器会请求目录写权限，之后保存会直接修改原文件。

其他浏览器或不希望授予写权限时，可以点击“导入目录”：选择一个 `Mods` 目录，
在内存副本中编辑，完成后点击“导出 ZIP”。ZIP 包含导入时的完整目录；未编辑的
DLL 等二进制文件按原始字节保留。应用不读取浏览器所选目录以外的文件。

直接写入模式下，每个文件在本次页面会话第一次被覆盖、删除或重命名前，都会备份到：

```text
Mods/.shadowbus-editor-backups/<UTC 时间>/<原相对路径>
```

同一文件在同一会话只备份一次。关闭或刷新页面后重新计为新会话。

## 支持的文件

| 模块 | 扫描路径 | 编辑方式 |
| --- | --- | --- |
| BossRush | `BossRush/<包名>/bossrush.json` | 完整已知字段表单、Boss/加护排序复制、隐藏 Boss、本地 AI 引用 |
| AIData | `AIData/deck|style|emote/*.csv` 与 `BossRush/<包名>/ai/.../*.csv` | Deck 基础列和 Tag 组、Style/Emote 表格 |
| CardMaster | `CardMaster/*.json` | 补丁列表、属性字典、技能并行字段、本地化和数组字段 |
| Format | `Format/*.json` | 所有赛制限制和单卡限制 |
| TwoPick | `TwoPick/*.json` | 职业、轮次、卡池、排除与权重规则 |
| AI 参考 | `AIData/ai_*.json`、BossRush Reference 目录 | 只读搜索 |

已知字段始终显示中文说明和原始 JSON/CSV 字段名。JSON 中未知字段可在每层的
“其他 / 未知字段”编辑器中查看与修改；CSV 的未知列会出现在表格末尾并原样写回。
应用内置当前导出的 418 个敌方角色和 61 个 Quest AI 映射，因此离线部署也有 ID
下拉列表；打开的 Mods 中若有 `BossRush/Reference/enemy_chara_ids.csv` 或
`manifest.json`，同 ID 的本地内容会覆盖内置目录。

## 卡牌名称与内置卡表

应用内置 5933 张卡的名称、职业、类型和费用攻防，因此所有卡牌 ID 输入框都会实时
显示对应卡名，不需要读取任何本地文件，离线和 GitHub Pages 部署行为一致：

- 列表和字典中的每个 ID 上方显示卡名，标题栏统计 `已识别 N/M`。
- 单个 ID 字段在输入框下方显示 `卡名 · 职业 · 类型 · 费用 攻/命`。
- 悬浮在 ID 上仍会显示卡图，并附带卡名和数值；点击卡图跳转 Shadowverse Portal。
- 闪卡 ID（末位为 1）自动归一到基础卡。
- 内置卡表中没有的 ID 会产生**警告**而不是错误，不阻止保存：卡表是某次导出的快照，
  出现新卡包时应重新生成而不是改配置。
- 当前打开的 CardMaster 文件中 `newCard` 创建的卡，按其 `localizationFields.CardName`
  和 `intFields` 解析（缺少卡名时沿用模板卡）。其他 CardMaster 文件不会被读取，因此
  ID 在 999990000 以上的自制卡显示为“自制卡牌”且不会触发未知 ID 警告。

卡表由 `scripts/build-card-catalog.mjs` 从游戏导出的 `card_names.csv` 生成
（`CardSkillExporter` 在 `Mods/CardMaster/Reference/` 下自动写出）。该 CSV 约 3.3 MB
且随导出机器的语言和卡池变化，因此不入库。游戏更新卡池后重新生成：

```powershell
cd WebEditor
npm run build:catalog                 # 使用默认游戏路径
npm run build:catalog -- <csv 路径>   # 或手动指定
```

脚本会丢弃闪卡重复行和导出机器自己的自制卡，只保留游戏数据，并在卡名含有无法编码
的字符、或出现未知职业/类型枚举时直接报错。不要手工编辑 `src/data/cards.generated.ts`。

BossRush 的复制、重命名和删除都会操作整个配置包（包含 `ai` 子目录）；重命名时会
询问是否同步 JSON 内部 `id`。其他 JSON 配置重命名时同样可选择同步内部 `id`。

## 校验与保存

- 错误会禁用保存，例如非法 ID、BossRush 无主线 Boss、TwoPick 使用原版 UI
  不支持的布局或轮次冲突。
- 警告不会阻止保存，但需要再次确认，例如 Boss 自定义牌组不是 40 张。
- 保存后格式化 JSON；CSV 保留原文件的 CRLF/LF 换行风格和标准引号转义。
- 页面存在未保存修改时，切换文件、目录或离开页面会提示确认。

## 游戏内生效时机

- BossRush：每次点击 BossRush 入口时重新扫描并应用配置。
- CardMaster：重新进入卡组列表时热重载。
- Format：重新进入新建卡组、卡组列表、卡组编辑、选牌组或 P2P 对应界面时重载。
- TwoPick：重新打开 P2P 建房规则选择时读取。
- AIData：自定义练习配置页点击“刷新 CSV”；BossRush 本地 AI 随 BossRush 配置重载。

正在进行的战斗不会被网页编辑器或游戏热重载强行重建。修改战斗数据后应退出当前
战斗并按上面的入口重新进入。

## 本地开发

需要 Node.js 22 或更高版本：

```powershell
cd WebEditor
npm install
npm run dev
```

测试和生产构建：

```powershell
npm test
npm run build
npm run build:catalog      # 仅在需要刷新内置卡表时运行，见上文
npm run build:reference    # 仅在需要刷新卡牌效果参考数据时运行，见上文
npm run build:han          # 仅在需要刷新简繁折叠表时运行（需要 Windows），见下文
npm run test:e2e:install  # 首次运行浏览器测试时下载 Chromium
npm run test:e2e
```

生产文件输出到 `WebEditor/dist`。Vite 的默认 base 为 `/Shadowbus/`，对应本仓库的
GitHub Pages 项目站点；若部署到其他仓库名，请修改 `vite.config.ts` 的 `base`。

## GitHub Pages

仓库包含 `.github/workflows/pages.yml`。在 GitHub 仓库设置中将 Pages 的 Source
设为 “GitHub Actions”，推送到 `main` 或 `master` 后，工作流会执行测试、构建并
部署 `WebEditor/dist`。工作流不需要密钥，也不会把用户的本地 Mods 内容发送到 GitHub。

## CardMaster 技能字段

`Skill`、`SkillTiming`、`SkillCondition`、`SkillTarget`、`SkillOption` 和
`SkillPreprocess` 这六个字段是并行的，且每个字段内含**两个列表**：

```text
字段 = 进化前列表 [ "//" 进化后列表 ]
列表 = 条目 ("," 条目)*
```

例如卡牌 100621020 的 `Skill` 是 `none//damage,heal`——进化前没有技能，进化后有
两个（造成伤害、回复）。这不是「三个逗号槽，第一个带变体」：游戏把两半分别放进
`NormalSkills` 和 `EvolutionSkills`，并且普通演出数组（`SkillEffectPath` 等）只按
进化前的条数分配，进化后的用 `EvoSkillEffectPath` 一套。唯一的例外是
`SkillEffectTargetType`：卡牌主数据里没有对应的 `evo_` 列，两个形态的目标类型都写在
这一个字段里，同样用 `//` 分隔（100621020 是 `none//single,single`），游戏把后半段作为
`EvoSkillEffectTargetType` 暴露出来。有 `evo_` 列的演出字段偶尔也带 `//`（全表只有
820531010 一张，`skill_effect_time` 写作 `2.5,0,0//0`），但那里的后半段只是重复了
`evo_skill_effect_time` 已有的值，所以参考面板取前半段给进化前形态，进化后照常读
`evo_` 列。

技能编辑器因此分成「普通形态」和「进化形态」两组，各自有独立的「新增技能」——
这一点很重要：若字段里已有 `//`，在末尾追加条目会落进**进化后**列表。可以随时
添加或移除进化形态，没有进化技能时不会写出 `//`。

六个字段必须在两个半段内都保持并行，编辑器会把以下情况判为错误：

- 某个字段含多个 `//`。
- 一部分字段带 `//`、另一部分不带（无法对齐）。
- 进化前或进化后的条目数在六个字段之间不一致。

技能编辑器是一个矩形矩阵（六个字段 × N 个技能），无法表达参差的条目数。游戏自带的
个别卡牌确实是参差的（例如 128111030 有五个字段是 4 普通 + 1 进化、`SkillPreprocess`
却是 3 + 2），这种内容可以正常读入和查看，但一旦编辑就会被补齐成矩形（短的字段补空
条目）。上面的校验会在保存前先把它标成错误，所以不会悄悄改掉。

技能字段可以写在 `stringAppendFields`（追加到模板卡已有技能之后）或
`stringChangeFields`（整体替换）中，编辑器顶部的「写入方式」可以切换，切换时会把
六个字段搬到另一个 map。追加模式需要前置逗号，否则会和模板卡的最后一个条目粘连；
若**模板卡本身**带进化技能，追加的内容必然落进进化形态，此时只能用完全替换。两个
map 同时写技能字段是合法的（游戏先替换再追加），但编辑器只结构化编辑
`stringChangeFields`，并会提示合并。

### 导入 DSL

「普通形态」和「进化形态」各有一个「导入 DSL」按钮，可以把从参考面板复制的
`(skill:...)(timing:...)` 直接粘进来：解析出的技能会**追加**到该形态现有技能之后，
不会覆盖。逗号分隔的多个技能组一次全部导入，弹窗里先按字段列出解析结果再确认。

DSL 比六个字段能表达的更多，因此两种情况会被整批拒绝而不是导入一半：

- **值里含逗号。** `(condition:count_over(me.hand,3))` 在 DSL 里是**一个**条件（括号
  是配对的），但六个字段本身就用逗号分隔条目，写进去会变成两条并让这个字段和其他五
  个错位。游戏把这类表达式放在 `skill_effect_condition`，本编辑器不管理该字段。
- **值里含 `//`。** 两个形态要分别导入到对应的那一组，而不是在一个条目里造出分隔符。

演出字段（`effect_path`、`se_path`、`effect_target_type` 等）不会被拒绝，但也不会被
导入——六个字段里没有它们的位置，弹窗会把这些键列出来提示，需要在「字符串替换」中
手动添加。

## 卡牌效果参考面板

右下角的悬浮按钮随时可以点开一个**始终在最顶层**的参考面板，用来一边写自己的效果、
一边翻游戏原本的卡牌。它不是模态框：打开时下层表单照常可点，也会浮在技能 DSL 编辑
弹窗之上，可以拖动标题栏移动、拖右下角改大小，关闭后会记住搜索词和面板位置。三种方式
都能收起：标题栏右上角的 ×、焦点在哪里都生效的 Esc（焦点在弹窗里时先关弹窗）、再次
点击悬浮按钮。标题栏的拖动只在按下的不是按钮、输入框或链接时才开始——否则捕获指针会
把随后的 pointerup 连同合成出的 click 一起改指到标题栏上，右上角的 × 就点不动了。

- 搜索卡名、效果文（去掉 `[u]`、`[ffcd45]`、`<<>>` 等标记后匹配）、原始技能字段，或
  直接输入 4 位以上的卡牌 ID；空格分隔的多个词是「与」的关系。结果行标注命中来源。
- 简体和繁体互相匹配：查询和被搜索的文本都先折叠成简体再比较，所以内置卡表是繁体导出
  时，用「从者」「伤害」也能查到「從者」「傷害」，反之亦然（见下文的折叠表）。
- 展开一张卡后显示卡图、卡名数值、效果文、六个技能字段的原文（含 `//`）、按形态拆开
  的每个技能，以及该卡的 `skill_effect_condition`（编辑器的六字段模型不含此字段，
  所以只展示不参与生成）。
- 一键复制：**每个技能各有一份自己的 DSL**，可以单独复制——通常只想抄一张卡里的某
  一个效果，而不是它的全部效果；此外还有整个形态合并后的 DSL（进化前、进化后各一份，
  因为 DSL 无法表达 `//`）、以及保留 `//` 的 `stringChangeFields` JSON 片段。复制出的
  DSL 可以直接用 CardMaster 编辑器的「导入 DSL」粘回去（见上文）。

数据由 `scripts/build-card-reference.mjs` 生成，联接两个导出：
`CardMaster_Default_backup.csv`（游戏自身的卡牌主数据，六个技能字段和演出列都是原文，
但文本只有本地化 ID）与 `Mods/CardMaster/Reference/card_names.csv`（有本地化后的效果
文）。只保留有技能或有效果文的非闪卡（当前 5890 张），生成
`src/data/cardReference.generated.ts`：

```powershell
cd WebEditor
npm run build:reference                                  # 使用默认游戏路径
npm run build:reference -- <backup csv> <card_names csv> # 或手动指定
```

这份数据比内置卡表大得多（blob 约 4.3 MB，其中演出字段占 0.7 MB），因此单独打包成一个
chunk，只在第一次打开面板时下载，不影响首屏。若 `card_names.csv` 来自旧版 mod（没有
`evo_skill_description` 列），只有进化后才有效果文的卡会缺文本，面板会提示重新导出。

`CardMaster_Default_backup.csv` 本身有一处编码损坏需要注意：导出它的工具用 DBCS 代码页
读了游戏的 UTF-8，日文的 `TribeNameId`（`TN_指揮官`、`TN_レヴィオン` 等）因此变成乱码；
乱码字符与字段末尾的逗号凑成非法字节对时，两者一起被替换成一个 `?`，该行就少了一个
分隔符——之后每一列都前移一位，行尾补一个空字段，**长度检查看不出来**。全表 251 行
基础卡（4%）如此，若不处理就会把 `rarity` 当成 `skill` 读。生成脚本用 `rarity` 必须是
数字来检测错位，在 `TribeNameId` 最后一个 `?` 处重新切开来修复，并要求修复后原不变式
成立才接受；多部族的卡把该字段用引号包住（`"TN_兵士,TN_レヴィオン"`），丢掉的是引号而
不是逗号，会一路串下去无法修复，只有 120231010 和 120241010 两张，按 ID 跳过。修复率
低于失败率时脚本直接报错，而不是猜。

### 简繁折叠表

`src/data/hanVariants.generated.ts` 是搜索用的繁体→简体字符表，由
`scripts/build-han-variants.mjs` 从 Windows 自带的 `LCMapStringEx`
（`LCMAP_SIMPLIFIED_CHINESE`）生成，因此无需安装依赖、也不联网：

```powershell
cd WebEditor
npm run build:han
```

U+4E00–U+9FFF 的 20992 个汉字中有 2473 个折叠到不同字符。表随代码入库，运行时不下载
任何东西——和内置卡表一样的约束。**只有这一个方向**：搜索时把查询和被搜索的文本都折叠
成简体再比较，一张表就能双向匹配；反方向是一对多（简体「发」既是「發」也是「髮」），
做不成表。脚本会断言运行时依赖的两条性质：映射逐字一对一（折叠不改变字符串长度），且
没有目标字本身又是源字（折叠一次和折叠两次等价）。

折叠只用于搜索，绝不用于写回文件：它是检索键，不是转换——游戏需要卡牌本来的写法。

## CardMaster 攻击特效

CardMaster 编辑器支持 `attackEffectFields`。每个字段使用 `[普通, 进化]` 两个值：`effectPath`（特效路径）、`se`（音效路径）、`moveType`（移动类型）、`effectEnginType`（`NONE`、`SHURIKEN`、`FLATOUT` 或 `SOLID`）和 `time`（秒）。留空字段会继续使用模板卡牌的攻击特效。

## 当前限制

- 编辑器只验证 Shadowbus 已知的结构约束，不解析卡牌技能 DSL 或 AI 表达式语义。
- 内置卡表是某次游戏导出的快照，语言取决于导出时的游戏语言（当前为繁体中文）；搜索时
  简繁互相折叠，所以用简体也能查（见上文的简繁折叠表），但显示出的卡名仍是导出时的
  写法。新卡包需要重新运行 `npm run build:catalog` 才会出现。
- 卡牌 ID 仍需手动输入或批量粘贴，尚无按卡名选卡的下拉界面；写效果时可用右下角的
  卡牌效果参考面板按卡名或效果文查找现成卡牌（见上文）。
- File System Access API 的直接目录读写只由 Chromium 系浏览器完整支持；其他浏览器
  使用导入目录和 ZIP 导出。
- `Reference`、BossRush `State`、`selected.txt` 和 AI 导出 JSON 只作为参考或运行状态，
  不由表单修改。
