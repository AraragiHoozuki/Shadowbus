# Shadowbus

**简体中文** | [English](README.en.md)

Shadowverse 国际服的单机化与卡牌 Mod 工具，基于 BepInEx 6 开发。

本仓库同时提供可部署到 GitHub Pages 的[一体化 Web 配置编辑器](WebEditor/README.md)，
支持用中文表单编辑 AIData、BossRush、CardMaster、Format 和 TwoPick 配置，并可直接
读写本地 Mods 目录或导出完整 ZIP。

## 功能

- 单机模式：支持主界面、无限制卡组编辑、CPU 对战和开包动画。
- 默认解锁全部卡牌、主战者皮肤、卡背与主界面背景，并在本地保存背景选择。
- 无限制卡组：忽略职业、卡牌数量和卡组张数限制，也可以加入衍生卡。
- 自定义练习：可指定对手卡组、职业、主战者和 AI CSV。
- 卡牌 Mod：修改或新增卡牌，并支持自定义卡图与文本。
- 卡组列表热重载：进入卡组列表时重新加载 `CardMaster` 配置。
- 主动技能：使用 `when_activate` 为场上随从添加“启动”按钮，可设置 PP 消费。
- 自定义技能：复制卡牌信息与能力，或在保留自身能力的同时获得目标能力。
- P2P 房间：房主生成加密连接码，另一名玩家粘贴连接码后可进行普通构筑 BO1。

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

## P2P 房间对战

P2P 模式不连接官方服务器，也不需要自建常驻公网服务器。房主在游戏运行期间临时监听一个 TCP 端口，并负责房间流程、消息转发和战斗结果裁定。

1. 双方安装相同版本的游戏、Shadowbus 和卡牌 Mod 数据。
2. 房主在原版房间界面选择普通构筑 BO1 并创建房间。
3. 房主点击复制房间号。剪贴板中会得到以 `SVP1-` 开头的加密连接码，而不是界面上显示的短房间号。
4. 客人在加入房间的连接码文本框中粘贴完整的 `SVP1-...` 密码；校验通过后确认按钮会自动启用。
5. 双方选择牌组并准备，之后按原版房间流程开始对战。

连接码包含房主地址、TCP 端口、协议版本和一次性随机令牌，并带有完整性校验。它等同于本次房间的密码，不要公开发布。关闭房间或退出游戏后，旧连接码即失效。

### 网络配置

P2P 模式不提供账号服务、房间列表、STUN 打洞或 TURN 中继。两名玩家必须满足下列任一条件：

- 位于同一局域网，连接码中使用房主的局域网地址。
- 房主有可入站的公网 IPv4，并在路由器和系统防火墙中放行所配置的 TCP 端口。
- 双方有可互访的 IPv6，房主将绑定地址和公布地址设置为对应 IPv6，并放行防火墙。
- 双方先加入 Tailscale、ZeroTier、Radmin VPN 等虚拟局域网，连接码中使用房主的虚拟网卡地址。

如果房主处于运营商 CGNAT 且没有可用 IPv6，应使用虚拟局域网；仅靠连接码无法穿透这种 NAT。

首次启动后，可在 `BepInEx/config/` 下本插件的配置文件中修改 `[P2P]`：

- `BindAddress`：房主监听的本地地址，IPv4 默认值为 `0.0.0.0`。
- `AdvertisedAddress`：写入连接码的地址。留空时优先使用明确配置的 `BindAddress`，否则自动选择一个同地址族的本机地址；跨公网或使用虚拟局域网时建议明确填写。使用 IPv6 时，两项都必须配置为 IPv6 地址。
- `Port`：房主监听的 TCP 端口，默认 `29600`。设置为 `0` 会随机选择端口，不适合固定端口转发。

目前支持普通构筑 Open Room BO1 和自定义规则的 Room Two Pick BO1；不支持 HOF、Windfall、Avatar、原版 Backdraft/Cube/Chaos Two Pick、BO3/BO5、观战、断线重连、奖励和反作弊。`Mods/TwoPick` 下每个 JSON 文件对应一个建房时可选的双选模式，`displayName` 是界面名称；双方分别在本地选牌，房主会把所选完整规则同步给访客，完成后再使用最终牌组进入匹配。战斗中断线时，仍在线的一方按断线胜利结算。每个游戏安装目录会在 `Mods/P2PIdentity.json` 保存独立玩家 ID，并在 `Mods/Profile.json` 保存玩家修改后的名称、称号、徽章和地区；不要把已经生成的身份文件复制给另一名玩家或第二个测试实例。

## 自定义练习

进入“单人 > 对战”，在对手职业选择页点击第九个“自定义卡组”图标。配置页可同时选择：

- 对手的本地无限制卡组、职业和已拥有主战者。
- 原作 AI 预设、逻辑等级和生命上限。
- 自定义 Deck、Style、Emote CSV。

自定义 CSV 分别放入 `Mods/AIData/deck/`、`style/` 和 `emote/`。文件需要保持原作对应 CSV 的列格式；配置项留为“使用原作预设”时，会使用当前职业所选预设的原始 AI 数据。配置页打开后新增文件，可点击“刷新 CSV”重新扫描。

### AI 主战者语音

Emote CSV 支持两个占位符，一份文件即可适配所有皮肤主战者：

- `VoiceID` 中的 `{LEADER}` 会替换成本场 AI 主战者的语音编号。
- `FaceID`、`MotionID`、`TextID` 中的 `{AUTO}` 会填入原作中与该语音配套的值，台词取当前语言版本。

```csv
ID,Category,FaceID,MotionID,VoiceID,TextID
0,14,{AUTO},{AUTO},{LEADER}_000_007,{AUTO}
1,13,{AUTO},{AUTO},{LEADER}_000_011,{AUTO}
```

`Mods/AIData/emote/ai_emote_sample.csv` 是一份可直接使用的现成文件。皮肤实际拥有哪些语音序号见 [Mods/AIData/README.md](Mods/AIData/README.md)，完整语法见 [Docs/AI_CSV_Guide.md](Docs/AI_CSV_Guide.md)。

### AI 行为配置

`BepInEx/config/` 下插件配置文件的 `[AI]` 段，控制 AI 如何处理原作 AI 数据没有描述的卡牌：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `StallTimeoutSeconds` | `30` | AI 无进展多少秒后强制结束回合，`0` 关闭 |
| `UnknownCardPlayBonusMin` | `0.5` | 无 AI 数据卡牌的最低出牌加分 |
| `UnknownCardPlayBonusMax` | `1.5` | 最高出牌加分，两项都填 `0` 则只保留防崩溃 |
| `PriceUnpricedCards` | `true` | 是否按标签为「有模拟标签、无计分标签」的法术和护符折价 |
| `RespectPlayLimitLocks` | `false` | 开启后，原作用 `playLimit` 锁定的卡牌跳过定价 |
| `LowLifeHealThreshold` | `10` | 主战者回复只在该生命值及以下计分 |

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

- 进化前卡图命名为 `<ResourceCardId>.png`。
- 进化后卡图命名为 `<ResourceCardId>_evo.png`；未提供时自动使用进化前卡图。
- 修改已有卡牌时，补丁会同步应用到其普通版和闪卡版，同时保留两个版本各自的身份字段。
- `stringArrayFields` 可用于替换 `SkillEffectPath`、`SkillSe`、`EvolEffectPath` 等 `string[]` 字段。

项目已提供以下扩展：

| 关键词 | 用途 |
| --- | --- |
| `when_activate` | 为己方场上随从提供主动发动时点；在 `SkillPreprocess` 中使用 `use_pp=N` 设置 PP 消费。 |
| `skill_geminize` | 复制目标随从的名称、类型、身材、文本与全部技能，并清除自身除该技能外的技能。 |
| `skill_acquire_skills` | 获得目标随从的全部技能与非身材 Buff，并保留自身技能；不会复制同类型技能或攻击力、生命值修正。 |
| `skill_mirror` | 成为法术或单体能力的指定目标时，使对应效果对使用者的随机随从再生效一次。 |

`skill_mirror` 支持 `all=true/false`、`include_self=true/false` 和 `ability=true/false`。前两项控制追加效果应用于随机一个或全部随从，以及镜像目标自身能否成为追加目标；`ability=true` 时，除法术外，其他明确指定本随从的单体能力也能触发，随机和群体效果不会触发。默认值依次为 `false`、`true` 和 `false`。

具体配置可参考 `Mods/CardMaster/` 下的示例和现有卡牌文件。更多技能时点说明见 [Mods/readme.md](Mods/readme.md)。

### CardMaster 攻击特效

`attackEffectFields` 可设置卡牌攻击时的普通/进化两套演出数据。字段值均为 `[普通, 进化]`：`effectPath`（特效路径）、`se`（音效路径）、`moveType`（移动类型）、`effectEnginType`（引擎类型 `NONE`/`SHURIKEN`/`FLATOUT`/`SOLID`）和 `time`（时长）。字段留空时保留模板卡牌原值。

```json
"attackEffectFields": {
  "effectPath": ["btl_attack_1", "btl_attack_2"],
  "se": ["se_btl_attack_1", "se_btl_attack_2"],
  "moveType": ["DIRECT", "DIRECT"],
  "effectEnginType": ["SHURIKEN", "SHURIKEN"],
  "time": [0.5, 0.5]
}
```

## 构建

```powershell
dotnet build Shadowbus.sln
```

构建产物位于 `bin/Debug/net46/Shadowbus.dll`。项目需要引用游戏的 `Assembly-CSharp.dll`；如果游戏安装位置不同，请调整 `Shadowbus.csproj` 中的 `HintPath`。

## 注意事项

- 本项目面向单机和 Mod 测试用途。
- 修改卡牌配置前建议保留备份。
- 正在进行的对局不会自动重建已生成的卡牌，测试配置时建议开始新对局。
