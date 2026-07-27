# Shadowbus

Shadowverse 国际服的单机化与卡牌 Mod 工具，基于 BepInEx 6 开发。

## 功能

- 单机模式：支持主界面、无限制卡组编辑、CPU 对战和开包动画。
- 默认解锁全部卡牌、主战者皮肤、卡背与主界面背景，并在本地保存背景选择。
- 无限制卡组：忽略职业、卡牌数量和卡组张数限制，也可以加入衍生卡。
- 自定义练习：可指定对手卡组、职业、主战者和 AI CSV。
- 卡牌 Mod：修改或新增卡牌，并支持自定义卡图与文本。
- 卡组列表热重载：进入卡组列表时重新加载 `CardMaster` 配置。
- 主动技能：使用 `when_activate` 为场上随从添加“启动”按钮，可设置 PP 消费。
- 自定义技能：复制卡牌信息与能力，或在保留自身能力的同时获得目标能力。

目前仅支持无限制卡组，AI 强化仍在开发中。

## 安装

### 使用成品包

从[百度网盘](https://pan.baidu.com/s/1iNJ7HMVR2cbV1aKvLzI2AA?pwd=7ejh)下载 BepInEx 和插件压缩包，解压到 Shadowverse 游戏根目录。

### 手动安装

1. 安装 BepInEx 6 Mono 32 位版本。
2. 将 `Shadowbus.dll` 和 `Newtonsoft.Json.dll` 放入 `BepInEx/plugins/`。
3. 将项目中的 `Mods` 文件夹复制到游戏根目录。

安装后的主要目录如下：

```text
Shadowverse/
├─ BepInEx/plugins/Shadowbus.dll
└─ Mods/
   ├─ AIData/
   │  ├─ deck/
   │  ├─ style/
   │  └─ emote/
   ├─ UnlimitedDecks/
   ├─ CardMaster/
   └─ CardImages/
```

## 自定义练习

进入“单人 > 对战”，在对手职业选择页点击第九个“自定义卡组”图标。配置页可同时选择：

- 对手的本地无限制卡组、职业和已拥有主战者。
- 原作 AI 预设、逻辑等级和生命上限。
- 自定义 Deck、Style、Emote CSV。

自定义 CSV 分别放入 `Mods/AIData/deck/`、`style/` 和 `emote/`。文件需要保持原作对应 CSV 的列格式；配置项留为“使用原作预设”时，会使用当前职业所选预设的原始 AI 数据。配置页打开后新增文件，可点击“刷新 CSV”重新扫描。

## 卡牌 Mod

卡牌补丁位于 `Mods/CardMaster/`：

- `.json` 文件会被加载，`.example` 文件仅作为示例。
- `newCard` 为 `false` 时修改 `templateCardId` 对应的卡牌。
- `newCard` 为 `true` 时以 `templateCardId` 为模板创建 `cardId` 对应的新卡。
- `intFields` 修改数值字段。
- `stringChangeFields` 替换技能等字符串字段。
- `stringAppendFields` 在原字符串后追加内容。
- `localizationFields` 修改卡名、能力文本和背景文本。

修改配置后进入卡组列表即可热重载。新增卡牌应使用未占用的卡牌 ID；卡图放在 `Mods/CardImages/`，并通过 `ResourceCardId` 引用。

项目已提供以下扩展：

| 关键词 | 用途 |
| --- | --- |
| `when_activate` | 为己方场上随从提供主动发动时点；在 `SkillPreprocess` 中使用 `use_pp=N` 设置 PP 消费。 |
| `skill_geminize` | 复制目标随从的名称、类型、身材、文本与全部技能，并清除自身除该技能外的技能。 |
| `skill_acquire_skills` | 获得目标随从的全部技能与非身材 Buff，并保留自身技能；不会复制同类型技能或攻击力、生命值修正。 |
| `skill_mirror` | 成为法术或单体能力的指定目标时，使对应效果对使用者的随机随从再生效一次。 |

`skill_mirror` 支持 `all=true/false`、`include_self=true/false` 和 `ability=true/false`。前两项控制追加效果应用于随机一个或全部随从，以及镜像目标自身能否成为追加目标；`ability=true` 时，除法术外，其他明确指定本随从的单体能力也能触发，随机和群体效果不会触发。默认值依次为 `false`、`true` 和 `false`。

具体配置可参考 `Mods/CardMaster/` 下的示例和现有卡牌文件。更多技能时点说明见 [Mods/readme.md](Mods/readme.md)。

## 构建

```powershell
dotnet build Shadowbus.sln
```

构建产物位于 `bin/Debug/net46/Shadowbus.dll`。项目需要引用游戏的 `Assembly-CSharp.dll`；如果游戏安装位置不同，请调整 `Shadowbus.csproj` 中的 `HintPath`。

## 注意事项

- 本项目面向单机和 Mod 测试用途。
- 修改卡牌配置前建议保留备份。
- 正在进行的对局不会自动重建已生成的卡牌，测试配置时建议开始新对局。
