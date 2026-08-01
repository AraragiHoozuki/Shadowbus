# BossRush 本地配置指南

Shadowbus 会恢复客户端中保留的 BossRush 界面和战斗流程，并以本地 JSON
代替已经关闭的官方服务器响应。BossRush 配置只引用游戏现有的卡牌、角色、
场地、BGM、AI 和技能 DSL，不会生成新美术或新增技能语义。

## 目录结构

```text
Mods/BossRush/
├─ default/
│  ├─ bossrush.json
│  └─ ai/
│     ├─ deck/
│     ├─ style/
│     └─ emote/
├─ my_challenge/
│  ├─ bossrush.json
│  └─ ai/
├─ Reference/
│  ├─ manifest.json
│  ├─ enemy_chara_ids.csv
│  ├─ quest_ai_setting.csv
│  ├─ deck/
│  ├─ style/
│  └─ emote/
├─ State/
│  └─ <config_id>.json
└─ selected.txt
```

- `BossRush` 下每个直接子目录代表一个配置包。
- 子目录内必须存在 `bossrush.json` 才会被识别。
- `Reference` 和 `State` 是保留目录，不会出现在游戏内配置选择列表。
- 只有一个有效配置时直接使用；有多个配置时，进入前使用游戏现有滚轮对话框选择。
- `selected.txt` 保存上次选择的配置 ID。
- 配置 ID 对应独立状态文件，不同配置的进度、牌组和能力不会互相覆盖。

## 完整结构

```json
{
  "schema_version": 5,
  "id": "my_challenge",
  "display_name": "My BossRush",
  "detail_title": "Challenge Rules",
  "detail_text": "Defeat all three bosses with one deck.\nChoose one upgrade before each battle.",
  "ui_theme": "grand_prix_1",
  "lobby_background": "",
  "default_player_life": 20,
  "initial_progress": 0,
  "abilities": [],
  "bosses": [],
  "hidden_boss": null
}
```

### 顶层字段

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `schema_version` | int | 否 | 当前示例版本为 `5`，用于默认配置迁移。 |
| `id` | string | 是 | 配置唯一 ID，同时作为状态文件名。不能包含 Windows 非法文件名字符。 |
| `display_name` | string | 否 | 游戏内 BossRush 卡片名称、大厅顶栏名称和多配置选择名称；为空时使用 `id`。 |
| `detail_title` | string | 否 | 大厅“详情”按钮打开的本地对话框标题；为空时使用 `display_name`。 |
| `detail_text` | string | 否 | 详情正文。JSON 中使用 `\n` 换行；为空时保留游戏原本的“无详情”提示。 |
| `ui_theme` | string | 否 | 大厅视觉主题，选项见下表。默认 `grand_prix_1`。 |
| `lobby_background` | string | 否 | 直接指定现有大厅背景资源名；非空时覆盖 `ui_theme`。不要带 `ui_` 前缀和 `.unity3d` 后缀。 |
| `default_player_life` | int | 否 | 新挑战的玩家初始及最大生命，必须大于 `0`，默认 `20`。 |
| `initial_progress` | int | 否 | 初始 Boss 索引，从 `0` 开始。通常保持 `0`。 |
| `abilities` | array | 否 | 可选 Buff 池，每次从符合条件的能力中随机生成候选。 |
| `bosses` | array | 是 | 主线 Boss，至少一个。数组顺序就是挑战顺序。 |
| `hidden_boss` | object/null | 否 | 主线全通后解锁的隐藏 Boss，字段与普通 Boss 相同。 |

## BossRush UI 主题

反编译源码中只有一个 `BossRushLobby` 布局 prefab，服务器响应也没有 UI 类型字段。
所谓主题切换由 Shadowbus 在本地替换大厅背景实现；按钮、Boss 面板、加护面板和进度球仍使用
原版 BossRush 布局。选择不同配置后，进入大厅时会立即应用该配置的主题。

| `ui_theme` | 背景资源 | 说明 |
| --- | --- | --- |
| `grand_prix_1` | `bg_gp_special_01` | 默认。与 BossRush 原版使用的 GrandPrixSpecial Atlas 最接近。 |
| `grand_prix_2` | `bg_gp_special_02` | Grand Prix 特殊背景第二种。 |
| `colosseum_1` | `bg_colosseum_01` | 竞技场背景第一种。 |
| `colosseum_2` | `bg_colosseum_02` | 竞技场背景第二种。 |
| `two_pick` | `bg_2pick` | 双选模式背景。 |
| `quest` | `bg_quest` | 普通 Quest 背景，资源缺失时也作为最终回退。 |
| `classic` | `bg_boss_rush` | 客户端源码中的原始名称；部分关服后的本地资源包不包含它。 |

也可以绕过预设直接指定已存在的背景：

```json
{
  "ui_theme": "grand_prix_1",
  "lobby_background": "bg_gp_special_02"
}
```

无效或缺失的背景会记录 `[BossRush]` 警告并回退到 `bg_quest`。自定义背景只引用已有资源，
Shadowbus 不会从图片文件创建新的 Unity 资源包。

## Boss 字段

```json
{
  "name": "First Boss",
  "enemy_class": 1,
  "enemy_chara_id": 1,
  "enemy_emblem_id": 0,
  "enemy_degree_id": 0,
  "bossrush_stage_id": 1,
  "battle3dfield_id": 1,
  "bgm_id": "",
  "enemy_life": 20,
  "recovery_point": 5,
  "enemy_skill": "(skill:draw)...",
  "enemy_skills": [],
  "enemy_skill_desc": "At the start of turn 1, draw 1 extra card.",
  "enemy_ai_id": 1,
  "player_first_turn": null,
  "player_start_pp": 0,
  "enemy_start_pp": 0,
  "player_start_field_card_ids": [],
  "enemy_start_field_card_ids": [],
  "enemy_sleeve_id": 3000011,
  "player_emotion_override": 0,
  "enemy_emotion_override": 0,
  "special_battle_id": "",
  "id_override_in_battle_log": "",
  "token_draw_effect_override": "",
  "special_token_draw_effect_override": "",
  "vs_effect_override": false,
  "class_destroy_effect_override": 0,
  "mission_parameter": {},
  "custom_deck_card_ids": [],
  "deck_csv": "",
  "style_csv": "",
  "emote_csv": "",
  "logic_level": 1,
  "use_inner_emote": true
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | Boss 名称，同时作为 Boss 技能详情标题。 |
| `enemy_class` | int | Boss 职业，见下方职业编号。 |
| `enemy_chara_id` | int | 已存在的角色/Leader ID。无效值可能导致角色资源加载失败。 |
| `enemy_emblem_id` | long | 徽章 ID。没有合适数据时使用 `0`。 |
| `enemy_degree_id` | long | 称号 ID。没有合适数据时使用 `0`。 |
| `bossrush_stage_id` | int | BossRush/Quest 阶段 ID。小于等于 `0` 时会归一化为 `1`。 |
| `battle3dfield_id` | int | 3D 场地 ID。小于等于 `0` 时会归一化为 `1`。 |
| `bgm_id` | string | 已存在的 BGM ID。空字符串使用当前客户端可用的默认行为。 |
| `enemy_life` | int | Boss 最大生命，小于等于 `0` 时使用 `20`。 |
| `recovery_point` | int | 击败该 Boss 后恢复的玩家生命，不会超过最大生命。 |
| `enemy_skill` | string | 敌方永久特殊技能，必须是客户端现有技能 DSL。兼容旧配置；多个技能可用逗号拼接。 |
| `enemy_skills` | string[] | 额外敌方技能数组。插件按顺序与 `enemy_skill` 合并后交给原版解析器，适合把多个长 DSL 分开书写。 |
| `enemy_skill_desc` | string | Boss 技能详情界面显示的说明，仅用于展示。 |
| `enemy_ai_id` | int | `quest_ai_setting.csv` 中的官方 Quest AI ID。无效时回退到首个可用官方 AI。 |
| `player_first_turn` | bool/null | `true` 强制玩家先手，`false` 强制敌方先手，`null` 保留随机先后手。 |
| `player_start_pp` | int | 玩家初始 PP，限制为 `0..10`。 |
| `enemy_start_pp` | int | 敌方初始 PP，限制为 `0..10`。 |
| `player_start_field_card_ids` | int[] | 战斗开始时直接放入玩家场上的卡牌 ID，按顺序处理，最多五张。 |
| `enemy_start_field_card_ids` | int[] | 战斗开始时直接放入敌方场上的卡牌 ID，按顺序处理，最多五张。 |
| `enemy_sleeve_id` | long | 敌方卡背 ID，默认 `3000011`；必须存在于本地 Sleeve Master 和资源中。 |
| `player_emotion_override` | int | 玩家表情/语音数据覆盖 ID。`0` 表示使用玩家 Leader 默认数据。 |
| `enemy_emotion_override` | int | 敌方表情/语音数据覆盖 ID。`0` 表示使用 `enemy_chara_id` 默认数据。 |
| `special_battle_id` | string | 剧情特殊战斗标识。当前客户端只有少数硬编码值有额外行为，例如 `"42"`。通常留空。 |
| `id_override_in_battle_log` | string | 卡牌显示/战斗日志 ID 替换，格式为 `原ID=目标ID`，多组用英文逗号分隔。 |
| `token_draw_effect_override` | string | Token 变身/抽取特效映射，格式为 `卡牌ID=特效路径`，多组用英文逗号分隔。 |
| `special_token_draw_effect_override` | string | `special_token_draw` 的特效映射，格式同上。 |
| `vs_effect_override` | bool | `true` 时使用客户端第二套开战 VS 特效和音效。 |
| `class_destroy_effect_override` | int | 敌方主战者破坏特效：`0` 默认、`1` 剧情样式 1、`2` 剧情样式 2。 |
| `mission_parameter` | object | 提供给现有 `mission_info` 技能过滤器的键值表；值使用字符串。 |
| `custom_deck_card_ids` | int[] | 敌方实际牌组。非空时优先于 `deck_csv` 和官方 AI 牌组。建议正好 40 张。 |
| `deck_csv` | string | 配置包目录下的本地 AI Deck CSV 相对路径，也可以是绝对路径。 |
| `style_csv` | string | 本地 AI Style CSV 路径。 |
| `emote_csv` | string | 本地 AI Emote CSV 路径。 |
| `logic_level` | int | 现有 AI 逻辑等级：`0` 弱、`1` 中、`2` 强；同时作用于官方 AI 和自定义牌组。 |
| `use_inner_emote` | bool | 是否使用 AI 内置表情逻辑；同时作用于官方 AI 和自定义牌组。 |

职业编号：

| ID | 职业 |
| --- | --- |
| `1` | Forestcraft / ELF |
| `2` | Swordcraft / ROYAL |
| `3` | Runecraft / WITCH |
| `4` | Dragoncraft / DRAGON |
| `5` | Shadowcraft / NECRO |
| `6` | Bloodcraft / VAMPIRE |
| `7` | Havencraft / BISHOP |
| `8` | Portalcraft / NEMESIS |

### `enemy_chara_id` 对应表

源码中明确固定的角色 ID 如下。`enemy_class` 应与角色所属职业一致：

| `enemy_chara_id` | 角色 | `enemy_class` |
| --- | --- | --- |
| `1` | Arisa / 亚里莎 | `1` Forestcraft |
| `2` | Erika / 艾莉卡 | `2` Swordcraft |
| `3` | Isabelle / 伊莎贝尔 | `3` Runecraft |
| `4` | Rowen / 罗文 | `4` Dragoncraft |
| `5` | Luna / 露娜 | `5` Shadowcraft |
| `6` | Urias / 尤里亚斯 | `6` Bloodcraft |
| `7` | Eris / 伊莉丝 | `7` Havencraft |
| `8` | Yuwan / 伊昂 | `8` Portalcraft |
| `500002` | Utsuroi Erika / 剧情艾莉卡 | 以导出表为准 |
| `500008` | Losaria / 罗莎丽亚 | 以导出表为准 |
| `500010` | Utsuroi 1 / 剧情角色 | 以导出表为准 |

完整 Leader、皮肤和剧情角色表不硬编码在源码中，而是来自运行时
`Data.Master.ClassCharacterList`。Master 加载完成后，插件会自动生成：

```text
Mods/BossRush/Reference/enemy_chara_ids.csv
```

列包括 `enemy_chara_id`、本地化角色名、职业、`skin_id`、资源路径、是否可用和是否为
3D Leader。制作配置时应以该文件为完整对应表，因为不同本地资源备份拥有的剧情角色和
联动皮肤可能不同。

`custom_deck_card_ids` 中的卡牌会逐张通过当前 `CardMaster` 校验。全部无效或列表为空时，
插件回退到 `enemy_ai_id` 对应的官方牌组。自定义牌组应与 `enemy_class` 匹配；插件不会
自动修正跨职业牌组。

### 先后手、初始 PP 与开局场面

这些设置属于每个 Boss，而不是整个配置包，因此不同关卡可以使用不同规则：

```json
{
  "player_first_turn": false,
  "player_start_pp": 0,
  "enemy_start_pp": 2,
  "player_start_field_card_ids": [100011020],
  "enemy_start_field_card_ids": [100411010, 100411010]
}
```

开局场面通过客户端已有的特殊战斗机制实现。插件为每张有效卡牌生成一次性技能：

```text
(skill:summon_token)(timing:when_battle_start)...
```

该技能在正式回合开始前执行，所以卡牌不会消耗 PP、不会从牌组抽取，也不会视为正常
“使用”卡牌。随从的入场曲通常不会触发，但召唤/进入场上相关能力可能触发，具体取决于
原卡技能实现。建议使用已在剧情特殊战斗中验证过的随从或护符。

- 卡牌必须存在于当前 `CardMaster`，无效 ID 会被忽略并写入日志。
- 每一方最多五张，超过部分会被截断。
- 场地空间不足时，后续召唤可能失败或由游戏规则处理。
- 法术通常不能作为持续在场卡牌使用，不建议放入开局场面数组。
- 玩家开局卡牌会显示为本场附加特殊能力，这是原版 BossRush 技能映射所需。

### 剧情特殊战斗扩展字段

这些字段来自 `DataMgr.SetSpecialBattleSetting` 和 `BossRushBattleData`，不会增加新技能
语义，只是将剧情战斗已有参数注入 BossRush：

```json
{
  "enemy_sleeve_id": 3000011,
  "player_emotion_override": 0,
  "enemy_emotion_override": 0,
  "special_battle_id": "",
  "id_override_in_battle_log": "100011010=100011020",
  "token_draw_effect_override": "100011020=cmn_token_draw_1",
  "special_token_draw_effect_override": "",
  "vs_effect_override": true,
  "class_destroy_effect_override": 1,
  "mission_parameter": {
    "custom_counter": "{me.main_place.class.turn}"
  }
}
```

- Token 特效值还可以使用 `特效路径:等待秒数`；只有本地已存在的特效路径才可用。
- `id_override_in_battle_log` 的解析器还会自动映射紧邻的进化后 ID。
- `special_battle_id="42"` 会触发客户端专门为某场剧情战斗硬编码的生命下限显示/特效；
  不应把未知数字当成通用模式。
- 原剧情参数中的 `banish_effect_override` 只在 `BattleType.Story` 下读取，因此没有作为
  BossRush 配置项提供。
- `skip_result` 会破坏 BossRush 自己的结算和进度流程，因此有意不开放。
- 特效、卡背和表情覆盖都依赖本地资源，配置错误时优先恢复为空值或默认值排错。

## Buff 字段

```json
{
  "ability_id": 117031020,
  "is_foil": false,
  "skill": "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=1)(option:none)(preprocess:remove_after_action=(count=1))",
  "special_ability_desc": "Increase maximum life by 5 and draw 1 extra card on turn 1.",
  "max_life_change": 5,
  "life_change": 5
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `ability_id` | int | 用于显示 Buff 卡面的现有卡牌 ID，必须存在于 `CardMaster`。 |
| `is_foil` | bool | 是否用闪卡形式显示。 |
| `skill` | string | 玩家获得的特殊技能 DSL。空字符串表示只有生命变化。 |
| `special_ability_desc` | string | 能力详情界面显示的说明，仅用于展示。 |
| `max_life_change` | int | 选择后改变最大生命。原版客户端未提供生命变化参数时由插件应用。 |
| `life_change` | int | 选择后恢复或扣除当前生命，结果限制在 `0..max_life`。 |

能力选择规则：

1. 第一个主线 Boss 前需要选择第一次 Buff。
2. 每击败一个 Boss，下一战前需要再选择一个 Buff。
3. 每次从尚未获得的有效能力中随机抽取最多三个，候选会写入状态文件，重新打开大厅不会重抽。
4. 只要还有未获得能力，候选中就不会出现已获得能力；未获得能力不足三个时，界面只显示剩余数量。
5. 所有有效能力至少取得一次后，才会从完整能力池随机抽取，允许重复取得并叠加技能/生命变化。
6. 已选择能力会在后续战斗中累积生效；重复取得同一能力会在列表中保留多份。
7. 主线结束后，隐藏 Boss 使用主线中已经取得的全部能力。

`skill` 与 `enemy_skill` 都直接传给游戏现有的 `SetSpecialBattleSetting`，不会进行二次
解释或自动纠错。建议先复制本地数据中已经出现过的完整技能字符串，只修改已知安全的
数值。语法错误可能在进入战斗后才暴露。

多个敌方技能建议使用数组，功能上与逗号拼接完全相同，不会创建新的技能类型：

```json
{
  "enemy_skill": "",
  "enemy_skills": [
    "(skill:draw)(timing:self_turn_start)...",
    "(skill:possess_ep_modifier)(timing:self_turn_start)..."
  ],
  "enemy_skill_desc": "Draw an extra card and recover EP under the configured conditions."
}
```

原版 BossRush 只提供一个敌方技能详情面板，所以 `enemy_skills` 中的所有技能共用一个
`enemy_skill_desc`。数组顺序只决定附加顺序，不会改变技能 DSL 自己的触发优先级规则。

## 反编译字段审查与边界

以下结论来自客户端现有的 `BossRushLobbyData`、`BossRushLobbyBossData`、
`BossRushBattleData`、`BossRushStartTask`、`BossRushHiddenBattleStartTask`、
`DataMgr.SetSpecialBattleSetting` 和 `SingleBattleMgr`：

- 原版 Boss 条目直接读取的字段已经全部开放：名称、职业、角色、徽章、称号、AI、阶段、
  场地、BGM、生命、技能、技能说明和胜利恢复生命。
- `player_first_turn`、双方初始 PP、双方附加技能、双方生命、卡牌日志替换、Token 特效、
  VS 特效和主战者破坏特效来自同一个特殊战斗设置对象，可在 BossRush 战斗类型下读取。
- `player_emotion_override`、`enemy_emotion_override`、敌方卡背以及 AI 的逻辑等级和内置表情
  也有明确的 BossRush 读取路径，因此作为配置项开放。
- `BossRushBattleData.MaxBattleCount` 和 `CurrentWinCount` 必须由 Boss 数组长度及本地进度推导，
  不能作为独立配置，否则结算、能力槽数量和大厅进度球会互相矛盾。
- 玩家当前生命、最大生命和已选能力属于整轮挑战状态。按 Boss 单独覆盖会破坏“生命跨关继承”，
  因此只通过 `default_player_life`、Buff 生命变化及 `recovery_point` 管理。
- 原版 `banish_effect_override` 在 `DestroyVfx.CreateBanishFileNamePair` 中明确限制为
  `BattleType.Story`，BossRush 设置该值也不会生效。
- `skip_result` 写入的是剧情特殊结算枚举，会跳过或替换死亡/结果流程，不兼容 BossRush 的
  胜负上报、进度保存和隐藏 Boss 解锁，因此不开放。
- Quest AI 的裸 `deck_id`、`style_id`、`emote_id` 依赖进入战斗前的异步 `LoadAICsv` 预加载。
  BossRush 原流程只预加载 `enemy_ai_id` 对应的一组 ID；直接替换 ID 可能引用未加载资源。
  因此牌组用 `custom_deck_card_ids` / `deck_csv`，行为和表情用本地 `style_csv` / `emote_csv`
  覆盖，不提供不安全的裸 ID 配置。
- `difficulty` 在这条单机 AI 路径中只被保存到恢复数据；实际强弱由 `logic_level` 映射，故不
  单独暴露一个看似有效但不改变 AI 决策的字段。
- 客户端没有“固定原始牌序”或“指定起手牌”的特殊战斗参数。实现它们需要侵入式修改洗牌和
  Mulligan 流程，超出当前复用现有语义的范围。开局场面继续使用已支持的
  `player_start_field_card_ids` / `enemy_start_field_card_ids`。
- 奖励等级、奖励列表和公告 ID 虽然存在于大厅响应，但本地模式按设计返回合法空奖励，不修改
  本地道具；详情文字由 `detail_title` / `detail_text` 提供，因此这些服务端运营字段不开放。

## AI 数据优先级

敌方牌组按以下顺序选择：

1. 非空且存在有效卡牌的 `custom_deck_card_ids`。
2. `deck_csv` 加载出的本地 AI Deck。
3. `enemy_ai_id` 对应的官方 Quest AI 牌组。

Style 和 Emote CSV 不改变牌组，只替换官方 AI 设置引用的行为/表情数据。路径相对于
当前配置包目录，例如：

```json
{
  "deck_csv": "ai/deck/boss_01.csv",
  "style_csv": "ai/style/boss_01.csv",
  "emote_csv": "ai/emote/boss_01.csv"
}
```

启动并完成 Master 加载后，插件会把客户端可读取的文本 AI Master 以及完整角色对应表导出到
`Mods/BossRush/Reference`。这些参考文件不包含 Boss 生命、场地、技能和关卡顺序；
原版的这些字段由服务器下发，关服后只能根据本地字段自行配置。

## 三关示例

下面示例使用官方 AI 牌组。若目标环境的 AI ID `1` 不可用，插件会记录日志并回退到
第一个可用 Quest AI。实际制作时建议参考自动生成的 `default/bossrush.json`，其中
包含四套完全本地化的 40 张敌方牌组。

```json
{
  "schema_version": 5,
  "id": "three_boss_sample",
  "display_name": "Three Boss Trial",
  "detail_title": "Three Boss Trial",
  "detail_text": "Use one deck to defeat all bosses.\nChoose a new ability before every battle.",
  "ui_theme": "grand_prix_2",
  "lobby_background": "",
  "default_player_life": 20,
  "initial_progress": 0,
  "abilities": [
    {
      "ability_id": 117031020,
      "is_foil": false,
      "skill": "",
      "special_ability_desc": "Maximum life +5 and recover 5 life.",
      "max_life_change": 5,
      "life_change": 5
    },
    {
      "ability_id": 100011020,
      "is_foil": false,
      "skill": "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=1)(option:none)(preprocess:remove_after_action=(count=1))",
      "special_ability_desc": "Draw 1 extra card on turn 1.",
      "max_life_change": 0,
      "life_change": 0
    },
    {
      "ability_id": 100011030,
      "is_foil": false,
      "skill": "(skill:possess_ep_modifier)(timing:self_turn_start)(condition:{me.usable_ep}<=0&&evolvable_turn=true)(target:character=me&target=inplay&card_type=class)(option:add_ep=1)(preprocess:remove_after_action=(count=1))(effect_path:btl_ep_cure_1)(se_path:se_btl_ep_cure_1)(effect_move_type:DIRECT_EPPANEL_SELF)(engine_type:SHURIKEN)(effect_time:0.5)(effect_target_type:single)",
      "special_ability_desc": "Recover 1 EP once when depleted.",
      "max_life_change": 0,
      "life_change": 0
    }
  ],
  "bosses": [
    {
      "name": "First Boss",
      "enemy_class": 1,
      "enemy_chara_id": 1,
      "enemy_emblem_id": 0,
      "enemy_degree_id": 0,
      "bossrush_stage_id": 1,
      "battle3dfield_id": 1,
      "bgm_id": "",
      "enemy_life": 20,
      "recovery_point": 5,
      "enemy_skill": "",
      "enemy_skill_desc": "No special skill.",
      "enemy_ai_id": 1,
      "custom_deck_card_ids": [],
      "deck_csv": "",
      "style_csv": "",
      "emote_csv": "",
      "logic_level": 1,
      "use_inner_emote": true
    },
    {
      "name": "Second Boss",
      "enemy_class": 4,
      "enemy_chara_id": 4,
      "enemy_emblem_id": 0,
      "enemy_degree_id": 0,
      "bossrush_stage_id": 1,
      "battle3dfield_id": 1,
      "bgm_id": "",
      "enemy_life": 25,
      "recovery_point": 5,
      "enemy_skill": "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=1)(option:none)(preprocess:remove_after_action=(count=1))",
      "enemy_skill_desc": "Draw 1 extra card on turn 1.",
      "enemy_ai_id": 1,
      "custom_deck_card_ids": [],
      "deck_csv": "",
      "style_csv": "",
      "emote_csv": "",
      "logic_level": 2,
      "use_inner_emote": true
    },
    {
      "name": "Final Boss",
      "enemy_class": 8,
      "enemy_chara_id": 8,
      "enemy_emblem_id": 0,
      "enemy_degree_id": 0,
      "bossrush_stage_id": 1,
      "battle3dfield_id": 1,
      "bgm_id": "",
      "enemy_life": 30,
      "recovery_point": 0,
      "enemy_skill": "(skill:draw)(timing:self_turn_start)(condition:{me.inplay.class.turn}=1)(target:character=me&target=deck&card_type=all&random_count=2)(option:none)(preprocess:remove_after_action=(count=1))",
      "enemy_skill_desc": "Draw 2 extra cards on turn 1.",
      "enemy_ai_id": 1,
      "custom_deck_card_ids": [],
      "deck_csv": "",
      "style_csv": "",
      "emote_csv": "",
      "logic_level": 2,
      "use_inner_emote": true
    }
  ],
  "hidden_boss": null
}
```

## 状态、失败与弃权

状态保存在 `Mods/BossRush/State/<id>.json`，通过临时文件替换写入，减少游戏意外
中断造成的损坏。状态包括进度、玩家牌组、生命、已选能力、总回合数、完成状态、
隐藏 Boss 状态和最佳记录。

- 战斗失败后使用原版失败流程；当前配置状态仍由本地文件管理。
- 点击“弃权/结束”会结束整轮挑战，清除进度、生命强化、已选能力、完成状态、
  隐藏 Boss 状态和已注册 BossRush 牌组。
- 弃权返回选择界面后，同一配置会显示“重新开始”，再次进入必须重新选择玩家牌组。
- 弃权不会删除最佳回合记录。
- 不建议在游戏运行时手工修改对应的状态文件。

若只想手工重置某个配置，可以在游戏关闭后删除对应的
`Mods/BossRush/State/<id>.json`；下次进入时会按配置重新创建。

## 默认配置

首次运行且不存在 `default/bossrush.json` 时，插件会生成一个可玩的默认包：

- 三个主线 Boss 和一个隐藏 Boss。
- 每名 Boss 使用内置基础卡组成的 40 张实际牌组。
- 三个主战前各选择一次 Buff。
- 每次随机显示最多三个未获得 Buff，全部取得后才允许重复。
- Boss 生命和技能逐关增强。
- 前两关胜利后恢复部分生命。
- 第一关固定玩家先手，第二关固定敌方先手，最终 Boss 额外拥有 1 初始 PP。

旧版本自动生成且未修改的单关 `Training Boss` 配置会自动升级。升级时重置旧挑战
进度，但保留玩家牌组记录和最佳回合记录。无法确定为自动生成的用户配置不会被覆盖。

## 校验与排错

常见日志前缀为 `[BossRush]`。

- 配置不出现：确认 `bossrush.json` 位于 `BossRush` 的直接子目录，且 `id` 非空。
- 配置被跳过：检查是否有重复 `id`，或 JSON 是否有尾逗号/缺失引号。
- Buff 不出现：确认 `ability_id` 存在于当前 `CardMaster`，并且仍有未选择能力。
- 敌方使用错误牌组：检查 `custom_deck_card_ids` 是否全部无效，以及本地 CSV 路径是否正确。
- AI CSV 无效：插件会记录警告并回退官方 AI；检查 `Reference` 中对应 CSV 的列结构。
- 角色或场地空白：换用已经在普通单人/练习模式中确认存在的 ID。
- 进入战斗时报技能错误：恢复到已知可用的完整技能 DSL，不要自行创造 `skill`、
  `timing`、`target` 或 `option` 名称。
- 弃权后仍显示旧按钮：确认已经替换为最新 `Shadowbus.dll`，并重新进入 Quest 选择页。

奖励响应固定为空，不会增加或扣除本地道具。
