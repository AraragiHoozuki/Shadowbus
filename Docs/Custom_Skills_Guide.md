# Shadowbus 自定义技能配置指南

本文说明 Shadowbus 新增的技能能力及其配置方法。当前扩展包括：

- 自定义时点：`when_activate`
- 自定义技能：`skill_geminize`
- 自定义技能：`skill_acquire_skills`
- 自定义技能：`skill_mirror`

这些内容写在 `Mods/CardMaster/` 下的卡牌补丁中。`.example` 文件仅作为示例，不会被加载；需要使用时请另存为 `.json`。

## 1. 技能字段

一条技能由以下 6 个并行字段组成：

| 字段 | 作用 |
| --- | --- |
| `Skill` | 技能类型，例如 `draw`、`skill_geminize` |
| `SkillTiming` | 发动时点，例如 `when_play`、`when_activate` |
| `SkillCondition` | 发动条件 |
| `SkillTarget` | 作用目标和选择方式 |
| `SkillOption` | 技能自身的选项 |
| `SkillPreprocess` | 技能发动前的处理，例如 PP 消费 |

每个字段都以逗号分隔多条技能，各字段中相同位置的内容属于同一条技能。使用 `stringAppendFields` 增加一条技能时，6 个字段必须同时追加，并保持索引一致：

```json
"stringAppendFields": {
  "Skill": ",skill_geminize",
  "SkillTiming": ",when_activate",
  "SkillCondition": ",character=me",
  "SkillTarget": ",character=both&target=inplay&card_type=unit&select_count=1",
  "SkillOption": ",none",
  "SkillPreprocess": ",none"
}
```

这里开头的逗号用于把新条目接在原卡牌数据之后。某个字段不需要参数时写 `none`，不要省略该位置。

常用的原生条件和目标参数如下：

| 参数 | 含义 |
| --- | --- |
| `character=me` | 技能拥有者一方 |
| `character=op` | 技能拥有者的对方 |
| `character=both` | 双方 |
| `target=self` | 技能拥有者自身 |
| `target=inplay` | 场上的卡牌 |
| `target=deck` | 牌堆 |
| `target=last_target` | 上一步选中的目标 |
| `card_type=unit` | 仅随从 |
| `card_type=all` | 所有卡牌类型 |
| `select_count=1` | 由玩家选择 1 个目标 |
| `random_count=1` | 随机选择 1 个目标 |
| `is_inplay=true` | 要求卡牌位于场上 |

这些是游戏原生技能系统的参数，可以和自定义技能组合使用。

## 2. `when_activate`

`when_activate` 是自定义发动时点，不是技能类型。把它写入任意技能的 `SkillTiming` 后，己方回合点击场上卡牌，可以通过详情面板中的“启动”按钮主动发动该技能。

可发动的卡牌必须位于己方场上且不是主战者。战斗结束、操作被锁定、观战或回放状态下不能发动。技能自身的 `SkillCondition` 和目标合法性仍会正常检查。

### PP 消费

在该技能对应的 `SkillPreprocess` 中使用：

```text
use_pp=N
```

`N` 是非负整数。例如 `use_pp=1` 表示发动时消费 1 PP。使用 `none` 时不消费 PP。当前 PP 不足时，启动按钮不可用。

如果同一张卡有多条当前可以发动的 `when_activate` 技能，点击一次会结算这些技能，PP 消费也会合计。选择目标、抉择和葬送等原生选择流程可以继续使用。

### 示例：消费 1 PP 抽 1 张牌

```json
"stringAppendFields": {
  "Skill": ",draw",
  "SkillTiming": ",when_activate",
  "SkillCondition": ",character=me",
  "SkillTarget": ",character=me&target=deck&card_type=all&random_count=1",
  "SkillOption": ",none",
  "SkillPreprocess": ",use_pp=1"
}
```

完整示例：[when_activate.example](../Mods/CardMaster/when_activate.example)

## 3. `skill_geminize`

让技能拥有者变为目标随从的复制体。

会复制：

- 目标当前的攻击力和生命值。
- 卡牌名称、类型/种族和能力文本。
- 普通形态与进化形态的技能。
- 普通描述和进化描述。

同时会清除技能拥有者原有的其他技能、Buff 和数值修正，但保留自身的 `skill_geminize`。结算后会重新注册常驻能力，并显示复制来源的 Buff 标签。

不会复制卡图、卡牌 ID、费用、职业或目标当前的进化状态。技能拥有者和目标都必须是随从，死亡目标无效。若传入多个目标，只使用第一个有效目标。

### 参数和选项

`skill_geminize` 没有自定义 `SkillOption`，请填写 `none`。目标通过原生 `SkillTarget` 设置：

| 目标写法 | 含义 |
| --- | --- |
| `character=me&target=inplay&card_type=unit&select_count=1` | 选择 1 个己方随从 |
| `character=op&target=inplay&card_type=unit&select_count=1` | 选择 1 个敌方随从 |
| `character=both&target=inplay&card_type=unit&select_count=1` | 选择双方场上的 1 个随从 |

### 示例

```json
"stringAppendFields": {
  "Skill": ",skill_geminize",
  "SkillTiming": ",when_activate",
  "SkillCondition": ",character=me",
  "SkillTarget": ",character=both&target=inplay&card_type=unit&select_count=1",
  "SkillOption": ",none",
  "SkillPreprocess": ",none"
}
```

完整示例：[skill_geminize.example](../Mods/CardMaster/skill_geminize.example)

## 4. `skill_acquire_skills`

获得目标随从的全部普通技能和进化技能，同时保留技能拥有者原有的技能。

该技能还会获取目标已有的非身材 Buff 和相关能力状态，但不会复制：

- 目标的 `skill_acquire_skills`。
- 由 `powerup` 或 `power_down` 产生的攻击力、生命值修正，例如 `+1/+1`。
- 卡牌名称、类型、文本和基础身材。

结算后会重新注册常驻能力，并显示复制来源的 Buff 标签。技能拥有者和目标都必须是随从，死亡目标无效。若传入多个目标，只使用第一个有效目标。

### 参数和选项

`skill_acquire_skills` 没有自定义 `SkillOption`，请填写 `none`。目标使用与 `skill_geminize` 相同的原生 `SkillTarget` 参数，可以通过 `character=me`、`character=op` 或 `character=both` 限制阵营。

### 示例

```json
"stringAppendFields": {
  "Skill": ",skill_acquire_skills",
  "SkillTiming": ",when_activate",
  "SkillCondition": ",character=me",
  "SkillTarget": ",character=both&target=inplay&card_type=unit&select_count=1",
  "SkillOption": ",none",
  "SkillPreprocess": ",none"
}
```

完整示例：[skill_acquire_skills.example](../Mods/CardMaster/skill_acquire_skills.example)

## 5. `skill_mirror`

当拥有该技能的随从成为符合条件的明确指定目标时，在原效果结算后，让该效果对效果使用者一方的随从再生效一次。

该技能是常驻能力，必须使用以下配置：

```text
SkillTiming:    when_change_inplay
SkillCondition: character=me&target=self&is_inplay=true
SkillTarget:    character=me&target=self
```

使用其他时点时，Mirror 不会注册为有效的场上常驻能力。有效时，随从身上会显示紫色常驻特效。

### `SkillOption`

多个选项使用 `&` 连接，例如：

```text
all=false&include_self=true&ability=true
```

| 选项 | 默认值 | 含义 |
| --- | --- | --- |
| `all` | `false` | `false` 时对使用者的 1 个随机存活随从追加效果；`true` 时对使用者的所有存活随从追加效果 |
| `include_self` | `true` | `true` 时允许原 Mirror 随从进入追加目标池；`false` 时排除它 |
| `ability` | `false` | `false` 时仅响应法术；`true` 时也响应随从、护符等来源的明确单体指定能力 |

布尔值建议使用 `true` 或 `false`，也支持 `1` 或 `0`。

`include_self` 只在 Mirror 随从属于效果使用者一方时有实际影响。例如己方能力指定己方 Mirror 随从时，可以用它决定追加效果是否还能再次命中该随从。

### 不会触发的情况

- 随机选中的目标。
- 群体效果或同时包含非明确目标的混合范围效果。
- Mirror 随从已离场或已死亡。
- 未开启 `ability` 时，由非法术卡牌产生的能力。
- `none`、选择、抉择、循环、调用其他技能和 `skill_mirror` 本身等流程控制技能。

Mirror 重复的是命中该随从的具体技能效果，不是重新打出整张卡。具有多条技能的卡牌会按每条符合条件的技能分别判断。追加效果直接执行，不会再次触发 Mirror，因此不会无限递归。

### 示例

```json
"stringAppendFields": {
  "Skill": ",skill_mirror",
  "SkillTiming": ",when_change_inplay",
  "SkillCondition": ",character=me&target=self&is_inplay=true",
  "SkillTarget": ",character=me&target=self",
  "SkillOption": ",all=false&include_self=true&ability=true",
  "SkillPreprocess": ",none"
}
```

完整示例：[skill_mirror.example](../Mods/CardMaster/skill_mirror.example)

## 6. 加载与测试

1. 在 `Mods/CardMaster/` 中创建或修改 `.json` 配置。
2. 进入游戏的卡组列表，使 CardMaster 配置热重载。
3. 新开始一场对战进行测试。

热重载不会修改进行中对战里已经实例化的卡牌。若只保留 `.example` 后缀，配置不会被加载。
