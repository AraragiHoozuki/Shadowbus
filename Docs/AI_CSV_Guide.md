# Shadowbus AI CSV 编写说明

本文说明自定义练习中使用的三类 AI CSV：

- `Deck CSV`：为卡牌提供数量及 AI 评价、模拟标签。
- `Style CSV`：调整 AI 的全局决策倾向。
- `Emote CSV`：定义 AI 可播放的动作、语音和台词。

这些 CSV 描述的是原作 AI 的决策数据，不是卡牌技能脚本。卡牌的实际效果仍由 CardMaster 和游戏技能系统决定。

## 1. 文件位置与基本规则

将文件放入游戏目录：

```text
Mods/AIData/
|-- deck/*.csv
|-- style/*.csv
`-- emote/*.csv
```

进入“自定义练习”配置页后选择对应文件。配置页已经打开时新增文件，可点击“刷新 CSV”重新扫描。某一项选择“使用原作预设”时，该项不会加载本地 CSV。

通用规则：

1. 文件必须保留第一行表头。Shadowbus 加载时会删除第一行，表头文字本身不参与字段匹配。
2. 建议保存为 UTF-8 CSV。
3. 字段中含有逗号、双引号或换行时，必须使用标准 CSV 双引号转义。例如表达式 `POW ( 2 , 3 )` 应写成 `"POW ( 2 , 3 )"`。
4. 关键词区分大小写。`All`、`ALLY`、`playBonus` 分别属于不同语法层，不能互换大小写。
5. 空整数会被解析为 `0`；无法解析的整数也会变为 `0`。
6. 空条件表示始终成立。非空条件的计算结果大于 `0` 时才视为成立。
7. CSV 列数不足通常会直接导致加载失败。错误信息可在 BepInEx 日志中查看。

## 2. Deck CSV

### 2.1 用途

Deck CSV 同时包含卡牌数量和每张卡的 AI 评价数据。当前 Shadowbus 自定义练习中，对手实际使用的卡组由 UI 中选择的无限制卡组决定；本地 Deck CSV 主要为其中的卡牌补充 AI 数据。建议让 `CardNum` 与所选卡组中的数量一致，并为卡组内每种卡牌各写一行。

如果不需要精细调教，先只填写前七列并将三项评价设为 `0`，再逐步增加标签。

### 2.2 列格式

固定前七列后，可以追加任意组 `Tag.Type / Tag.Arg / Tag.Condition`。文件最后必须再保留一个未使用的空列。

| 序号 | 列 | 含义 |
| --- | --- | --- |
| 1 | `CardID` | 卡牌 ID，普通写法为游戏中的基础卡牌 ID。 |
| 2 | `UseCommon` | 只有精确字符 `〇` 表示启用；其他内容和空白均表示关闭。 |
| 3 | `CardName` | 备注用卡名，不参与卡牌查找。 |
| 4 | `CardNum` | 此卡数量；空白或非法值为 `0`。 |
| 5 | `BattleBonus` | 卡牌留在场上、交换或战斗时的基础评价表达式。越大越有价值。 |
| 6 | `PlayBonus` | 打出该卡的基础评价表达式。越大越倾向打出。 |
| 7 | `Priority` | 行动顺序评价表达式。越大越倾向优先处理该卡。 |
| 8+ | `TagN.Type` | AI 模拟标签关键词，例如 `playBonus`。 |
| 9+ | `TagN.Arg` | 此标签的参数。不同标签的参数格式不同。 |
| 10+ | `TagN.Condition` | 此标签生效的条件表达式。空白表示始终生效。 |
| 最后一列 | `End` | 解析器保留列，内容应留空。 |

`UseCommon=〇` 时，游戏会先从 Ally Common AI 数据取得该卡的通用评价，再合并本行；否则从 Basic AI 数据取得通用评价。它不是“是否为中立卡”或“是否允许重复”的开关。

`CardID` 超过 9 位时，原作会把最后两位当作卡包版本号，并根据当前 Rotation 最新卡包筛选。自定义文件通常应使用 9 位以内的正常卡牌 ID，除非明确需要原作的版本筛选机制。

### 2.3 最小示例

没有标签时仍要保留最后的空列：

```csv
CardID,UseCommon,CardName,CardNum,BattleBonus,PlayBonus,Priority,End
100011010,〇,哥布林,3,0,0,0,
```

加入一个简单的出牌加分标签：

```csv
CardID,UseCommon,CardName,CardNum,BattleBonus,PlayBonus,Priority,Tag1.Type,Tag1.Arg,Tag1.Condition,End
100011010,〇,哥布林,3,0,0,0,playBonus,2,NOW_REST_PP >= 1,
```

上例表示：当模拟时当前剩余 PP 至少为 1，打出此卡额外获得 `2` 点 AI 评价。示例 ID 仅用于展示格式，实际文件应换成目标卡牌的正确 ID。

### 2.4 常用 Type

| Type | 含义 |
| --- | --- |
| `playBonus` | 为打出此卡增加评价。`Arg` 通常是数值表达式。 |
| `playBonusRate` | 乘算打出评价。`1` 不变，`1.5` 提高 50%。 |
| `battleBonus` | 增加该卡在场上或战斗中的评价。 |
| `battleBonusRate` | 乘算战斗评价。 |
| `priority` | 增加行动优先级。 |
| `target` | 描述此卡效果的合法/理想目标过滤条件。 |
| `ignoreTarget` | 描述 AI 应忽略的目标。 |
| `mlgKeep` | 增加起手保留倾向。 |
| `mlgChange` | 增加起手换牌倾向。 |
| `fanfareBonus` | 为入场曲模拟结果增加评价。 |
| `evoBonus` | 为进化此卡增加评价。 |
| `attackBonus` | 为使用此卡攻击增加评价。 |
| `playSkip*` | 满足参数和条件时跳过某种出牌模拟。 |
| `*Damage` / `*Heal` | 告诉 AI 在对应时点模拟伤害/回复。 |
| `*Destroy` / `*Banish` / `*Bounce` | 模拟破坏/消灭/返回手牌。 |
| `*Buff` / `*Evo` | 模拟强化或进化。 |
| `*Token` / `*Draw` | 模拟生成衍生物或抽牌。 |
| `*AttachTag` / `*RemoveTag` | 在 AI 的虚拟场面中附加/移除另一个 AI 标签。 |

标签名一般由“触发时点 + 模拟动作”组合：

| 片段 | 含义 |
| --- | --- |
| `play` / `fanfare` | 打出卡牌 / 入场曲处理。 |
| `attack` / `clash` | 攻击 / 交战时。 |
| `afterAttack` / `afterClash` | 攻击或交战结算后。 |
| `summon` / `otherSummon` | 自身或其他卡被召唤时。 |
| `evo` / `otherEvo` | 自身或其他卡进化时。 |
| `turnStart` / `turnEnd` | 回合开始 / 结束。 |
| `damaged` / `otherDamaged` | 自身或其他卡受到伤害时。 |
| `lastword` / `break` | 谢幕曲相关 / 被破坏时。 |
| `buff` / `heal` / `discard` | 获得强化 / 回复 / 弃牌时。 |
| `otherPlay` / `otherAttack` / `otherLeave` | 其他卡被打出 / 攻击 / 离场时。 |
| `changeInplayImmediate` | 在场状态变化后立即重新评价常驻效果。 |
| `B` / `Bonus` | 加算评价；例如 `allyPlayB`、`enemyBattleB`。 |
| `BonusRate` | 乘算评价倍率。 |
| `DamageCut` / `DamageClip` | 减伤 / 伤害上限模拟。 |
| `RecoverPp` | 回复 PP。 |
| `SetLeaderMaxLife` | 修改主战者生命上限。 |
| `SubtractCountdown` | 减少倒数。 |
| `AttackableCount` | 修改可攻击次数。 |
| `ActivateCount` | 记录或评价能力发动次数。 |

`Arg` 没有统一格式：`playBonus` 可直接使用一个表达式，而 `playDamage`、`fanfareSelect`、`*AttachTag` 等类型会创建各自的专用参数解析器。不要根据标签名猜测复杂参数顺序。实际制作复杂 AI 时，最可靠的流程是从 `Mods/AIData/ai_deck.json` 中找到效果相近的原作卡牌，复制其 `Type/Arg/Condition` 后再修改 ID、数值和过滤条件。

### 2.5 完整 Type 关键词

以下是当前游戏解析器接受的全部 `Tag.Type` 字符串。缩写和拼写均按原作保留，例如 `allyPlayB`、`memBattleB`、`playoutAtkB`，不能擅自改成长名称。

```text
accelerate, addCardToPlayoutPlayPtn, afterAttackBanish, afterAttackDraw, afterAttackEvo, afterAttackHeal, afterClashDamage, afterClashHeal
allyDiscardBonus, allyPlayB, allyPlayoutDamageBonus, attackableClass, attackActivateCount, attackAddDeck, attackAttachTag, attackAttackableCount
attackBanish, attackBonus, attackBreakAttackTwice, attackBreakDamage, attackBreakEvo, attackBreakRecoverPp, attackBuff, attackByLife
attackDamage, attackDamageClip, attackDestroy, attackDiscard, attackEvo, attackHandBuff, attackHeal, attackKiller
attackQuick, attackRemoveSkill, attackRemoveTag, attackSetStatus, attackShield, attackSubtractCountdown, attackToken, banishActivateCount
banishAttachTag, banishBonus, battleBonus, battleBonusRate, bounceBonus, bounceDamage, break, breakActivateCount
breakAddStack, breakAttachTag, breakBeforePlay, breakBuff, breakDamage, breakDestroy, breakFirst, breakHeal
breakLast, breakRecoverAttackableCount, breakRecoverPp, breakSetLeaderMaxLife, buffActivateCount, buffBonus, buffBuff, buffDamage
buffDestroy, buffDraw, buffEvo, buffHeal, buffRecoverPp, buffRush, buffShield, buffToken
burialRite, cantBeAttacked, changeInplayAttachTag, changeInplayCannotAttack, changeInplayCannotPlay, changeInplayImmediateDamageClip, changeInplayImmediateDamageCut, changeInplayImmediateDamageModifier
changeInplayImmediateIndestructible, changeInplayImmediateLifeLowerLimit, changeInplayImmediateRemoveByBanish, changeInplayImmediateRemoveByDestroy, changeInplayImmediateShield, changeInplayImmediateUntouchable, changePpTotalBuff, choiceBrave
choiceTransform, clashBanish, clashBonus, clashBuff, clashDamage, clashDamageClip, clashDestroy, clashHeal
clashKiller, clashRemoveSkill, clashRemoveTag, clashShield, clashSpellboost, clashToken, condChoice, costBonus
crystalize, damagedActivateCount, damagedBonus, damagedBuff, damagedCantUnderAttack, damagedDamage, damagedHeal, damagedToken
discardDamage, discardedBonus, discardedToken, discardHeal, earthRite, emoOnAtk, emoOnDestroy, emoOnEvo
emoOnPlay, emoOnTurnEnd, enemyBattleB, enemyBattleBonusRate, enemyEvoBonus, enemyPlayBonus, enhance, evoActivateCount
evoAddDeck, evoAddStack, evoAttachTag, evoAttackableCount, evoBanish, evoBonus, evoBounce, evoBuff
evoChangeCost, evoChoice, evoDamage, evoDamageCut, evoDestroy, evoDiscard, evoDrain, evoEvo
evoGuard, evoHandBuff, evoHandMetamorphose, evoHandPlus, evoHandSelect, evoHeal, evoKiller, evolvedAttackable
evolvedAttackableCount, evolvedSkill, evolveToOther, evoMetamorphose, evoQuick, evoReanimate, evoRecoverPp, evoRush
evoSetLeaderMaxLife, evoSetStatus, evoShield, evoSubtractCountdown, evoToken, evoTokenDraw, fanfareAddCemetery, fanfareAddDeck
fanfareAttachStyle, fanfareAttachTag, fanfareAttackableCount, fanfareBanAttack, fanfareBanish, fanfareBonus, fanfareBonusInSimulation, fanfareBounce
fanfareBuff, fanfareChangeClass, fanfareChangeCost, fanfareChangeTribe, fanfareChoice, fanfareCopyTag, fanfareDamage, fanfareDamageClip
fanfareDamageCut, fanfareDestroy, fanfareDiscard, fanfareDrain, fanfareEvo, fanfareForceTargeting, fanfareGuard, fanfareHandBuff
fanfareHandMetamorphose, fanfareHandSelect, fanfareHeal, fanfareIgnoreGuard, fanfareKiller, fanfareMetamorphose, fanfareModifyConsumeEp, fanfareNotBeAttacked
fanfareQuick, fanfareReanimate, fanfareRecoverAttackableCount, fanfareRecoverPp, fanfareRemoveGuard, fanfareRemoveSkill, fanfareRush, fanfareSelect
fanfareSetMaxStatus, fanfareShield, fanfareSneak, fanfareSpellboost, fanfareSubtractCountdown, fanfareSummonHandCard, fanfareToken, fanfareTokenDraw
fanfareUntouchable, firstEvo, forceBerserk, forceEmoOnDestroy, forceImmediateAttack, fusion, fusionBonus, fusionDraw
fusionMetamorphose, generateTag, getOffEvo, getOffMetamorphose, getOn, getOnBanish, getOnDamage, getOnEvo
giveSkill, handBonus, handPlus, healActivateCount, healAttachTag, healBuff, healDamage, healEvo
healHeal, healToken, ignoreBreak, ignoreFanfareBonus, ignoreTarget, lastwordAddCemetery, lastwordAddDeck, lastwordAttachTag
lastwordBanish, lastwordBuff, lastwordDamage, lastwordDamageClip, lastwordDestroy, lastwordDraw, lastwordEvo, lastwordHeal
lastwordMetamorphose, lastwordReanimate, lastwordRemoveSkill, lastwordSetStatus, lastwordShield, lastwordSubtractCountdown, lastwordToken, leaveAttachTag
leaveBanish, leaveBonus, leaveDamage, leaveHeal, leaveToken, memBattleB, memberBattleBonusRate, memberEvoBonus
mlgChange, mlgKeep, modifyHeal, necromance, necromanceActivateCount, necromanceAddCemetery, necromanceAttachTag, necromanceDamage
necromanceHeal, noInstantAttack, noNormalEvo, noSkipAttack, oneMoreLastword, otherAttackAttachTag, otherAttackBuff, otherAttackDamage
otherAttackHeal, otherAttackRemoveTag, otherAttackToken, otherBanishAddCemetery, otherBanishBonus, otherBanishToken, otherBreakBonus, otherDamagedBanish
otherDamagedDamage, otherDamagedHeal, otherDamagedSetLeaderMaxLife, otherDamagedSubtractCountdown, otherEnhanceEvo, otherEvoBanish, otherEvoBuff, otherEvoDamage
otherEvoEvo, otherEvoShield, otherEvoSubtractCountdown, otherEvoToken, otherLeaveBonus, otherLeaveDamage, otherLeaveToken, otherPlayAttachTag
otherPlayBounce, otherPlayBuff, otherPlayDamage, otherPlayDestroy, otherPlayEvo, otherPlayQuick, otherPlayRecoverPp, otherPlayRemoveTag
otherPlayToken, otherSummonAddCemetery, otherSummonAttachTag, otherSummonBanish, otherSummonBuff, otherSummonDamage, otherSummonDamageClip, otherSummonDamageCut
otherSummonDestroy, otherSummonDrain, otherSummonDraw, otherSummonEvo, otherSummonGuard, otherSummonHeal, otherSummonKiller, otherSummonQuick
otherSummonRush, otherSummonSubtractCountdown, otherSummonUntouchable, plagueCity, playActivateCount, playAddCemetery, playAddDeck, playAttachTag
playAttackableCount, playBanAttack, playBanish, playBonus, playBonusInSimulation, playBonusRate, playBounce, playBuff
playChangeClass, playChangeCost, playChangeTribe, playChoice, playCopyTag, playDamage, playDamageClip, playDamageCut
playDestroy, playDiscard, playDrain, playDraw, playEvo, playGuard, playHandBuff, playHandMetamorphose
playHandSelect, playHeal, playIgnoreGuard, playKiller, playLimit, playMetamorphose, playModifyConsumeEp, playNotBeAttacked
playoutAtkB, playoutDamageB, playoutNextTurn, playPlus, playptnBaseStatsRate, playptnBonus, playQuick, playReanimate
playRecoverPp, playRemoveSkill, playRush, playSelect, playSetLeaderMaxLife, playSetMaxStatus, playShield, playSkip
playSkipIfEvo, playSkipWithAction, playSkipWithActionIfEvo, playSkipWithEvo, playSneak, playSpellboost, playSubtractCountdown, playSummonHandCard
playToken, playTokenDraw, playUntouchable, priority, puppetAttack, rallyCountPlus, reanimateBonus, reanimateEvo
reincarnation, removeByDestroy, removeSkill, resonanceDamage, resonanceHeal, resonanceKiller, selfAndOtherEvoAddCemetery, selfAndOtherEvoAttachTag
selfAndOtherEvoBounce, selfAndOtherEvoDamage, selfAndOtherEvoDestroy, selfAndOtherEvoDraw, selfAndOtherEvoHeal, selfAndOtherEvoShield, selfAndOtherEvoToken, selfAndOtherEvoTokenDraw
setAITribe, stack, summonActivateCount, summonAttachTag, summonBanAttack, summonBanish, summonBuff, summonDamage
summonDestroy, summonEvo, summonHeal, summonQuick, summonRush, target, turnEndActivateCount, turnEndAddDeck
turnEndAttachTag, turnEndBanAttack, turnEndBanish, turnEndBounce, turnEndBuff, turnEndDamage, turnEndDamageClip, turnEndDamageCut
turnEndDestroy, turnEndDiscard, turnEndDraw, turnEndEvo, turnEndGuard, turnEndHeal, turnEndMetamorphose, turnEndRemoveTag
turnEndSetLeaderMaxLife, turnEndShield, turnEndSubtractCountdown, turnEndToken, turnStartAttachTag, turnStartDamage, turnStartDamageCut, turnStartShield
turnStartSubtractCountdown
```

## 3. Style CSV

### 3.1 列格式

Style CSV 每行固定六列：

| 序号 | 列 | 含义 |
| --- | --- | --- |
| 1 | `ID` | 行 ID，仅用于标识，建议在文件内唯一。 |
| 2 | `Category` | 生效职业。只能使用下表列出的大小写。 |
| 3 | `Priority` | 同一 `Category + Type` 的覆盖优先级。只保留最高值；最高值相同时多行并存。 |
| 4 | `Type` | 策略关键词。 |
| 5 | `Arg` | 策略参数或表达式。 |
| 6 | `Cond` | 生效条件。空白表示始终生效。 |

有效 `Category`：

| 关键词 | 职业 |
| --- | --- |
| `All` | 全职业 |
| `Elf` | 精灵 |
| `Royal` | 皇家护卫 |
| `Witch` | 巫师 |
| `Dragon` | 龙族 |
| `Necromance` | 唤灵师，注意不是 `Necromancer` |
| `Vampire` | 暗夜伯爵 |
| `Bishop` | 主教 |
| `Nemesis` | 复仇者 |

未知 `Category` 不会报错，而是退回 `All`，因此拼写错误可能意外影响全部职业。

### 3.2 Type 与 Arg

| Type | Arg 格式 | 效果 |
| --- | --- | --- |
| `epValue` | `数值表达式` | 加算 EP 的评价价值。 |
| `modUnitRate` | `倍率表达式` | 乘算随从/单位评价；默认基准为 `1`。 |
| `unitBonus` | `数值表达式` | 加算随从/单位评价。 |
| `playptnBonus` | `数值表达式` | 加算整个出牌方案的评价。 |
| `playBreak` | `过滤条件 ; ...` | 匹配手牌卡时停止/截断该出牌方案。 |
| `barrierBonus` | `数值表达式` | 每层屏障加算此评价，最终乘以屏障层数。 |
| `allyPlayB` | `过滤条件 ; ... ; 加分 [; USE_MIN]` | 对匹配的己方打出卡加算评价；末尾 `USE_MIN` 表示把该值作为上限/最小约束处理。 |
| `allyPlayBonusRate` | `过滤条件 ; ... ; 倍率` | 对匹配的己方打出卡乘算评价。 |
| `disableLethalCheck` | 留空 | 条件成立时关闭通常的斩杀检查；`Arg` 不参与计算。 |
| `delayTurnEndTime` | `最短秒数 ; 最长秒数` | 在范围内随机增加结束回合前的等待时间。 |
| `setReferenceId` | `@来源ID ; @目标ID` | 建立 AI 内部 ID 替换/引用表。此策略不检查 `Cond`。 |
| `setReferenceTribe` | `#组名 ; @卡牌ID ; @卡牌ID ...` | 建立自定义 AI 组名到多个卡牌 ID 的映射。此策略不检查 `Cond`。 |
| `emoOnTurnEnd` | `ALLY/ENEMY/BOTH ; CategoryID` | 指定 AI 在对应方回合结束时使用的 Emote 分类。 |
| `emoOnTurnStart` | `ALLY/ENEMY/BOTH ; CategoryID` | 指定 AI 在对应方回合开始时使用的 Emote 分类。 |
| `emoOnLeaderDamaged` | `CategoryID 表达式` | AI 主战者受伤时使用的 Emote 分类。 |
| `playerEmoOnTurnEnd` | `ALLY/ENEMY/BOTH ; CategoryID` | 玩家侧在对应回合结束时使用的 Emote 分类。 |
| `playerEmoOnTurnStart` | `CategoryID 表达式` | 玩家侧回合开始时使用的 Emote 分类。 |
| `playerEmoOnLeaderDamaged` | `CategoryID 表达式` | 玩家主战者受伤时使用的 Emote 分类。 |
| `moveFirstBonus` | `ALL/LEAST_VALUE/ALLY_ATTACK_FOLLOWER ; 加分` | 给方案中的第一个行动加分；可限定为全部、最低价值卡先行动、或后续存在己方随从攻击。 |
| `gameStartAttachTag` | 见下方 | 对局开始时为 AI 虚拟卡附加 Deck Tag。 |

未知 `Type` 会变成无效果的 `None`。以上关键词全部区分大小写。

`gameStartAttachTag` 使用以下结构：

```text
过滤条件 ; ... ; ALL_SELECT ; 移除时点 ; { Tag.Type } { Tag.Arg } { Tag.Condition }
```

例如，在对局开始时为所有己方随从附加一个持续到回合结束的 `playBonus` 标签：

```text
ALLY ; FOLLOWER ; ALL_SELECT ; WHEN_TURNEND ; { playBonus } { 2 } { }
```

这是高级接口，花括号、三个标签块以及它们前面的 ` ; ` 都有固定意义。复杂用法建议从原作 `ai_style.json` 中复制相近行。

### 3.3 示例

```csv
ID,Category,Priority,Type,Arg,Cond
1,All,100,unitBonus,1,IS_ON_FIELD == 1
2,All,100,modUnitRate,1.2,OWN_COST >= 7
3,All,100,emoOnTurnStart,ALLY ; 2001,NOW_TURN == 1
4,Dragon,200,playptnBonus,3,IS_AWAKE == 1
```

同一个 `Category + Type` 若需要同时保留多行，应使用相同的最高 `Priority`。如果想让本地规则覆盖原作同类规则，使用更高的 `Priority`，例如 `1000`。

## 4. Emote CSV

### 4.1 列格式

Emote CSV 每行固定六列：

| 序号 | 列 | 含义 |
| --- | --- | --- |
| 1 | `ID` | Emote 命令 ID。建议文件内唯一；最近播放过的三个 ID 会被降低再次随机到的概率。 |
| 2 | `Category` | Emote 分类 ID。Style 或卡牌 AI 标签通过此数字选择一组 Emote。 |
| 3 | `FaceID` | 表情编号，通常为 `1` 到 `10`。 |
| 4 | `MotionID` | 主战者动作编号，见下表。 |
| 5 | `VoiceID` | 语音资源 ID 字符串。空白表示不指定语音。 |
| 6 | `TextID` | 原作 Emote 文本 Master ID，不是直接显示的文字。空白表示不显示文本。 |

同一 `Category` 可以有多行，触发该分类时会随机选择一行。`VoiceID`、`TextID` 必须引用当前主战者和游戏资源中实际存在的 ID；任意填写中文台词不会直接显示。制作带语音的 Emote 时应从相同主战者的原作数据复制这两个值。

### 4.2 FaceID

| 数值 | 内部名称 |
| --- | --- |
| `1` - `10` | `skin_01` - `skin_10` |

并非所有主战者都提供全部表情资源。不存在的编号可能没有变化或产生资源错误。

### 4.3 MotionID

| ID | 内部名称 | 大致用途 |
| --- | --- | --- |
| 1 | `idle` | 待机 |
| 2 | `positive` | 正面反应 |
| 3 | `negative` | 负面反应 |
| 4 | `extra` | 特殊动作 |
| 5 | `damage` | 受伤 |
| 6 | `think` | 思考 |
| 7 | `greet` | 问候 |
| 8 | `shock` | 惊讶 |
| 9 | `positive_2` | 第二正面反应 |
| 10 | `negative_2` | 第二负面反应 |
| 11 | `extra_2` | 第二特殊动作 |
| 12 | `extra_3` | 第三特殊动作 |
| 13 | `negative_2_a` | 负面动作变体 |
| 14 | `damege_a` | 受伤动作变体，原作拼写如此 |
| 15 - 17 | `extra_1_a` - `extra_1_c` | 特殊动作 1 变体 |
| 18 - 20 | `extra_2_a` - `extra_2_c` | 特殊动作 2 变体 |
| 21 | `z_extra_2` | Z 系列特殊动作 |
| 22 | `z_damage` | Z 系列受伤 |
| 23 | `z_greet` | Z 系列问候 |
| 24 | `z_idle` | Z 系列待机 |
| 25 | `z_negative` | Z 系列负面反应 |
| 26 | `z_negative_2` | Z 系列第二负面反应 |
| 27 | `z_negative_2_a` | Z 系列负面变体 |
| 28 | `z_positive` | Z 系列正面反应 |
| 29 | `z_positive_2` | Z 系列第二正面反应 |
| 30 | `z_shock` | Z 系列惊讶 |
| 31 | `z_think` | Z 系列思考 |

动作是否存在仍取决于所选主战者。最稳妥的做法是复制该主战者原作 Emote 的 `FaceID/MotionID` 组合。

### 4.4 内置 Category

下列分类会被游戏内置判断直接触发，也可在 Style 中引用：

| Category | 内部名称 | 触发含义 |
| --- | --- | --- |
| 1 | `UnexpectedPlayResult_Good` | 一次出牌结果比预期好。 |
| 2 | `UnexpectedPlayResult_Bad` | 一次出牌结果比预期差。 |
| 3 | `LongThinking` | 思考时间过长。 |
| 11 | `UnexpectedBattleResult` | 战斗结果出乎预期。 |
| 12 | `ReverseDisAdv` | 从劣势转为非劣势。 |
| 13 | `CheckMated` | 判断即将败北。 |
| 14 | `HuntDown` | 判断可以斩杀对手。 |
| 15 | `RemainTooPP` | 回合结束时剩余较多 PP。 |
| 21 | `SpellBanishOverExpected` | 法术消灭收益高于预期。 |
| 22 | `FatalAttack` | 致命攻击。 |
| 31 | `OpponentRemainTooPP` | 对手结束回合时剩余较多 PP。 |
| 32 | `OpponentReverseDisAdvFail` | 对手未能扭转劣势。 |
| 33 | `OpponentGetGreatMerit` | 对手本回合获得很大收益。 |
| 34 | `OpponentPlayGiant` | 对手打出高场面价值卡。 |
| 35 | `OpponentBanishSpell_Good` | 对手消灭法术收益好。 |
| 36 | `OpponentBanishSpell_Bad` | 对手消灭法术收益差。 |
| 37 | `OpponentWellHealing` | 对手进行大量回复。 |
| 101 | `EliminatedAllyLegion` | 己方特定类型随从被清除。 |
| 102 | `EnoughUnit` | 己方场上随从充足。 |
| 401 | `Awake` | 接近或进入觉醒。 |
| 501 | `EnoughGrave` | 墓场数量充足。 |

游戏通常把 `Category <= 1000` 视为内部 Emote，只有 `useInnerEmote` 开启时才会播放。Shadowbus 当前自定义练习会开启此项。自定义分类建议从 `2001` 开始，避免与原作内置分类冲突。

### 4.5 示例

以下示例创建一个无语音、无文字的自定义动作，并通过前面的 Style 示例在第一回合开始时触发：

```csv
ID,Category,FaceID,MotionID,VoiceID,TextID
20001,2001,1,7,,
20002,2001,2,2,,
```

## 5. AI 表达式语法

Deck 的三项基础评价、Tag 的 `Arg/Condition` 以及 Style 的 `Arg/Cond` 共用同一套表达式解析器。

### 5.1 空格非常重要

解析器只按空格拆分词元，因此运算符、括号和逗号两侧都要留空格：

```text
NOW_TURN >= 5
( OWN_ATK + OWN_LIFE ) * 2
HAND_COUNT ( FOLLOWER ) >= 2
POW ( 2 , 3 )
```

以下写法不能可靠解析：

```text
NOW_TURN>=5
(OWN_ATK+OWN_LIFE)*2
HAND_COUNT(FOLLOWER)
```

### 5.2 运算符

| 关键词 | 含义 |
| --- | --- |
| `+` `-` `*` `/` `%` | 加、减、乘、除、取余 |
| `>` `>=` `<` `<=` `==` | 比较；成立为 `1`，否则为 `0` |
| `&` | 逻辑且；两边都大于 `0` 时为 `1` |
| `\|` | 逻辑或；任一边大于 `0` 时为 `1` |
| `max` `min` | 二元中缀最大/最小，例如 `OWN_ATK max OWN_LIFE` |
| `(` `)` `,` | 函数和分组符号，必须作为独立词元 |

`Condition` 最终结果大于 `0` 才成立。没有独立的一元逻辑非运算符；在过滤关键词前加 `!` 表示排除，例如 `!GUARD`、`!@100011010`。

### 5.3 特殊词元

| 写法 | 含义 |
| --- | --- |
| `@123456789` | 卡牌/技能 ID 词元。需要 ID 的参数必须加 `@`。 |
| `#group_name` | 文本词元。文本中不能包含空格。 |
| `!FOLLOWER` | 对过滤关键词取反，即“不是随从”。 |
| `!@123456789` | 排除指定 ID。 |
| `表达式 ; 表达式` | 在 `Arg` 中分隔多个独立参数；分号不是普通算术运算符。 |

### 5.4 过滤关键词含义

过滤关键词用于 `target`、`playBreak`、`allyPlayB` 及大量专用 Tag。多个以分号分隔的过滤器通常同时满足才通过。

| 类别 | 关键词与含义 |
| --- | --- |
| 阵营 | `ALLY` 己方、`ENEMY` 对方、`BOTH` 双方、`SELF` 标签持有者自身。表达式中使用 `ENEMY`，不是内部枚举名 `OPPONENT`。 |
| 卡牌类型 | `FOLLOWER` 当前视为随从、`FOLLOWER_CARD_TYPE` 原始随从卡、`SPELL` 当前视为法术、`SPELL_CARD_TYPE` 原始法术卡、`AMULET` 护符、`CHANT_FIELD` 倒数护符、`CLASS` 主战者。 |
| 位置 | `IN_HAND` 手牌中、`IN_PLAY` 场上、`ALLY_CLASS` 己方主战者、`ENEMY_CLASS` 对方主战者。 |
| 职业 | `NEUTRAL`、`ELF`、`ROYAL`、`WITCH`、`DRAGON`、`NECROMANCER`、`VAMPIRE`、`BISHOP`、`NEMESIS`。 |
| 类型/特征 | `LEGION` 指挥官、`LORD` 士兵，以及 `ARTIFACT`、`MANARIA`、`MACHINE`、`NATURE`、`LEVIN`、`LOOT`、`HERO`、`ARMED`、`SCHOOL`、`CHESS` 等原作类型。 |
| 能力 | `GUARD` 守护、`RUSH` 突进、`QUICK` 疾驰、`DRAIN` 吸血、`SNEAK` 潜行、`KILLER` 必杀、`UNTOUCHABLE` 不可被能力指定、`IGNORE_GUARD` 无视守护。 |
| 状态 | `EVOLVED` 已进化、`ATTACKABLE` 可攻击、`ATTACKED` 已攻击、`DAMAGED_FOLLOWER` 已受伤随从、`NO_DAMAGED_FOLLOWER` 未受伤随从、`BUFFED_FOLLOWER` 获得身材强化的随从。 |
| 上下文 | `PLAYED_CARD` 当前打出的卡、`ATTACKER` 攻击者、`CLASH_TARGET` 交战对象、`EVOLVER` 进化者、`SELECTED_TARGET` 第一组已选目标、`SECOND_SELECTED_TARGET` 第二组已选目标、`TRIGGER` 当前触发者。 |
| 选择 | `ALL_SELECT` 全选、`RANDOM_SELECT` 随机一个、`RANDOM_MULTI_SELECT` 随机多个、`TARGET_SELECT` 由目标逻辑选择、`FIRST_SELECT` 第一个、`OLDEST_SELECT` 最早者。 |
| 处理方式 | `DESTROY` 破坏、`BANISH` 消灭、`TEMP` 临时、`PERM` 永久、`ADD` 加算、`SET` 设值、`TURN` 本回合、`GAME` 整场。 |
| 时点 | `WHEN_PLAY`、`WHEN_DESTROY`、`WHEN_ATTACK`、`WHEN_CLASH`、`WHEN_DAMAGED`、`WHEN_EVO`、`WHEN_SUMMON`、`WHEN_HEAL`、`WHEN_LEAVE`、`WHEN_TURNEND` 等。 |
| 数值过滤 | `LIFE_INF n` 表示生命 `> n`，`LIFE_SUP n` 表示生命 `< n`，`LIFE_EQL n` 表示生命 `== n`；`ATK_*`、`COST_*`、`BASE_COST_*` 同理。`INF/SUP` 的方向容易误解，请按这里的实际实现使用。 |
| ID | 单独写 `@卡牌ID` 表示只匹配该 ID；写 `!@卡牌ID` 表示排除。 |

完整可解析参数关键词如下。某些词只适用于特定 Tag，能被解析不代表在所有 `Arg` 中都有意义。

```text
ACCELERATE, ADD, ALL, ALL_DAMAGE, ALL_SELECT, ALL_SPELLBOOST, ALLY, ALLY_AMULET
ALLY_ATTACK_FOLLOWER, ALLY_CLASS, AMULET, ANY_TRIBE, ARMED, ARTIFACT, ATK_EQL, ATK_INF
ATK_SUP, ATTACK_DAMAGE, ATTACKABLE, ATTACKED, ATTACKER, BANISH, BANISH_LOGIC, BANISHED_TARGET
BANQUET, BASE_COST_EQL, BASE_COST_INF, BASE_COST_SUP, BEFORE_PLAYPTN, BISHOP, BOTH, BOUNCE_LOGIC
BUFFED_FOLLOWER, CANDIDATE, CANT_ATTACK, CHANT_FIELD, CHESS, CHOICED_TARGET, CLASH_TARGET, CLASS
CONSUME_EP_ZERO, COST_EQL, COST_INF, COST_SUP, COUNTDOWN_EQL, CRYSTALIZE, CRYSTALIZE_HOLDER, DAMAGE_CLIP
DAMAGE_CUT, DAMAGE_LOGIC, DAMAGED_FOLLOWER, DEFAULT_LOGIC, DESTROY, DESTROY_LOGIC, DESTROYED_CARD, DESTROYED_IN_CURRENT_TURN
DESTRUCTIBLE, DIVIDED_SELECT, DRAGON, DRAIN, ELF, ENEMY, ENEMY_CLASS, ENHANCED
EVOLVED, EVOLVED_FOLLOWER, EVOLVER, FIELD, FILTER_END, FIRST_SELECT, FIRST_SUMMON_FOLLOWER_IN_PLAYPTN, FIRST_TURN
FOLLOWER, FOLLOWER_CARD_TYPE, FOOD, FORCE_TARGETING, GAME, GETOFF_CARD, GUARD, HELLBOUND
HERO, IGNORE_GUARD, IGNORE_IN_BATTLE, IGNORE_IN_FUSION, IN_HAND, IN_PLAY, KILLER, LAST_DRAW_CARD
LASTWORD, LATEST_DRAW_CARD, LATEST_SUMMON_CARD, LEAST_VALUE, LEGION, LEVIN, LIFE_EQL, LIFE_INF
LIFE_SUP, LOOT, LORD, MACHINE, MANARIA, MAX_ATTACK, MAX_ATTACK_LOGIC, MAX_COST
MEDUSA, METAMORPHOSE_LOGIC, MIN_ATTACK, MIN_COST, NATURE, NECROMANCER, NEMESIS, NEUTRAL
NEWER, NEXT_PLAY, NO_DAMAGED_FOLLOWER, NO_SKILL, NONE, NOT_BE_ATTACKED, NOT_COUNTDOWN_AMULET, NOW
OLDEST_FOLLOWER, OLDEST_SELECT, OTHER_FOLLOWER, OTHER_OLDEST_HAND_CARD_TYPE, PERM, PLAY_COUNT_EQL, PLAYED, PLAYED_CARD
PLAYPTN, PREVIOUS_TURN_ATTACKED, QUICK, RANDOM_MULTI_SELECT, RANDOM_SELECT, REAL_SKILL_TARGET, REVERSE_DISCARD_LOGIC, REVERSE_TARGET
ROMELIA_TARGET, ROYAL, RUSH, SCHOOL, SECOND_SELECTED_TARGET, SECOND_TARGET_SELECT, SELECTED_TARGET, SELECTED_TARGET_ID
SELECTED_TARGET_SIDE, SELF, SET, SKILL_DAMAGE, SNEAK, SPELL, SPELL_CARD_TYPE, SPELL_DAMAGE
SUMMON_AMULET, SUMMON_FOLLOWER, TARGET_SELECT, TEMP, TOKEN_DRAW, TRIGGER, TURN, TYRANT_ORDER_LOGIC
UNBANISHABLE, UNION_BURST, UNTOUCHABLE, USE_MIN, VAMPIRE, WHEN_ALLY_TURNEND, WHEN_ALLY_TURNSTART, WHEN_ATTACK
WHEN_CLASH, WHEN_DAMAGED, WHEN_DESTROY, WHEN_EVO, WHEN_HEAL, WHEN_LEAVE, WHEN_NEXT_TURNEND, WHEN_OPPONENT_TURNEND
WHEN_OPPONENT_TURNSTART, WHEN_PLAY, WHEN_PLAY_DAMAGE, WHEN_PLAY_DESTROY, WHEN_SUMMON, WHEN_TURNEND, WHITE_RITUAL, WHITEFROST_WHISPER_LOGIC
WITCH
```

### 5.5 变量关键词

变量直接返回一个数值。`IS_*` 变量返回 `1` 或 `0`。

| 命名 | 含义 |
| --- | --- |
| `OWN_*` | 当前标签持有者的攻击、生命、费用或基础身材。 |
| `ALLY_*` / `ENEMY_*` | 相对标签持有者的己方/对方数据。 |
| `MEMBER_*` | 己方场上随从数据。 |
| `REST_PP` | 模拟完整出牌方案后的预测剩余 PP。 |
| `NOW_REST_PP` | 当前虚拟场面立即可用的 PP。 |
| `HAND_COUNT_E` / `DECK_COUNT_E` | 对手手牌数 / 对手牌堆数。 |
| `ALLY_UNIT_MIN/MAX` | 己方随从的最低/最高 AI 评价，不是随从数量。 |
| `ENEMY_UNIT_MIN/MAX` | 对方随从的最低/最高 AI 评价。 |
| `IS_F_ADV` / `IS_F_DISADV` | 当前场面评价为优势 / 劣势。 |
| `IS_BERSERK` / `IS_TO_BE_BERSERK` | 当前状态狂乱 / 执行模拟方案后将进入狂乱。 |
| `IS_FANFARE` | 当前入场曲评价未被忽略；不等同于“这张卡拥有入场曲”。 |
| `IS_USED_EVO` | 原作实现实际返回“本回合仍可使用普通进化”的反向状态，使用前应对照原版数据测试。 |

`LEADER_DEF_LIFE` 和 `LEADER_CURRENT_LIFE` 虽然能被解析器识别，但当前计算器没有对应实现，会返回无效结果，不应使用。

完整变量列表：

```text
ACCELERATE_COST, ALLY_ATTACKABLE_ATK_MAX, ALLY_EP, ALLY_INPLAY_MAX_ATK, ALLY_MAX_ATTACKABLE_LIFE, ALLY_MAX_PP, ALLY_NON_ATTACKABLE_ATK_MAX, ALLY_UNIT_MAX
ALLY_UNIT_MIN, ATTACK_TARGET_ATK_MAX, BASE_SKILL_COUNT, CHOICE_TRANSFORM_COST, CONSUME_EP, COUNTDOWN, DECK_COUNT_E, DEFAULT_DAMAGE
EARTH_RITE_COUNT, ENEMY_ATK_SUM, ENEMY_EP, ENEMY_MAX_ATK, ENEMY_MAX_PP, ENEMY_MIN_ATK, ENEMY_UNIT_MAX, ENEMY_UNIT_MIN
ENHANCE_COST, FIELD_SPACE, GRAVE_COUNT, HAND_COUNT_E, HAND_MAX_COST, HAND_MIN_COST, INPLAY_ATTACK_SUM_TO_LEADER, IS_ABLE_EVO
IS_ACCELERATE, IS_ALLY, IS_ALLY_FIRST, IS_ATTACK_LEADER, IS_ATTACKED, IS_AWAKE, IS_BARBAROSSA, IS_BERSERK
IS_CRYSTALIZE, IS_DAMAGED, IS_DELAY_HEAL, IS_DRAIN, IS_ENEMY, IS_ENHANCED, IS_EVO_TURN, IS_EVOLVED
IS_EVOLVING, IS_F_ADV, IS_F_DISADV, IS_FANFARE, IS_FIRST_TURN, IS_FORCE_TARGETING, IS_GET_ON, IS_GUARD
IS_IGNORE_GUARD, IS_IN_HAND, IS_IN_PLAYPTN, IS_IN_SIMULATION, IS_KILLER, IS_LEADER, IS_LETHAL, IS_MEDUSA
IS_NOT_ATTACK_YET, IS_NOT_BE_ATTACKED, IS_ON_FIELD, IS_ONEMORELASTWORD_TAGGED, IS_OWNER_TURN, IS_PLAYOUT_ATTACKER, IS_QUICK, IS_RESONANCE
IS_RUSH, IS_SKILL_REMOVED, IS_SKILL_SUMMONED, IS_SNEAK, IS_TO_BE_BERSERK, IS_UNTOUCHABLE, IS_USED_EVO, JUST_BEFORE_TURN_DAMAGE
KILLER_ATTACK_VALUE, LAST_HEAL_AMOUNT, LAST_LIFE, LEADER_CURRENT_LIFE, LEADER_DEF_LIFE, MAX_ATTACKABLE_COUNT, MEMBER_ATK_SUM, MEMBER_LIFE_SUM
MEMBER_MAX_LIFE, NECROMANCE_COUNT, NECROMANCED_COUNT_IN_GAME, NEXT_PLAY_PRIORITY, NOW_REST_PP, NOW_TURN, OWN_ATK, OWN_BASE_ATK
OWN_BASE_LIFE, OWN_COST, OWN_LIFE, PLAY_ACTOR_ENHANCE_COST, PLAYPTN_COUNT, RALLY_COUNT, REINCARNATION_MAX, REST_PP
SKYBOUND_ART_COUNT, SPELLBOOST, SUMMON_DRUNKEN_ATK_MAX, SUPER_SKYBOUND_ART_COUNT, UNION_BURST_COUNT, USED_EP_COUNT, USED_PP_COUNT, USED_STACK_COUNT
```

### 5.6 函数关键词

函数使用空格分隔的括号和逗号。常见形式：

```text
HAND_COUNT ( FOLLOWER )
INPLAY_COUNT ( ALLY )
POW ( OWN_ATK , 2 )
CEILING ( OWN_LIFE / 2 )
```

命名规则：

- `*_COUNT` 返回符合参数/过滤条件的数量。
- `*_NAME_COUNT` 按基础卡牌 ID 去重后计数。
- `PLAYED_*`、`BROKEN_*`、`FUSION_*` 查询本回合或整场记录。
- `IS_*` 返回 `1/0`。
- `EVAL_TARGETING_*`、`EVAL_ALL_*`、`EVAL_RANDOM_*` 计算指定、全体或随机效果的预期收益。
- `LIFE`、`ATTACK`、`BASE_COST` 读取匹配目标的属性。
- `POW`、`CEILING`、`FLOOR`、`RANDOM` 分别为幂、向上取整、向下取整和稳定随机评价。

`EVAL_*` 函数的参数与具体效果、目标选择和伤害类型强相关，不存在通用签名。制作这类表达式时应复制同类型原作 Tag 的完整函数调用，再逐项修改。

完整函数列表：

```text
ADD_HAND_COUNT, ADDED_DECK_COUNT_IN_GAME, ATTACK, BANISH_COUNT, BASE_COST, BEFORE_PLAYPTN_COUNT, BOUNCE_COUNT, BROKEN_COST_SUM
BROKEN_COUNT, BROKEN_NAME_COUNT, BUFF_COUNT, BURIAL_COUNT, CEILING, DAMAGE_COUNT, DECK_COUNT, DECK_MAX_COST
DECK_NAME_COUNT, DESTROYED_COUNT, DISCARD_COUNT, DRAW_COUNT, EMOTE_PLAY_COUNT, EVAL_ALL_BANISH, EVAL_ALL_BOUNCE, EVAL_ALL_BUFF
EVAL_ALL_COUNTDOWN, EVAL_ALL_DAMAGE, EVAL_ALL_DESTROY, EVAL_ALL_HEAL, EVAL_ALL_METAMORPHOSE, EVAL_ALL_MULTI_DAMAGE, EVAL_ATTACK_REMOVE, EVAL_COUNTDOWN
EVAL_DIVIDED_DAMAGE, EVAL_ECHO_DAMAGE, EVAL_INSTANT_ATTACK, EVAL_LEADER_DAMAGE, EVAL_LEADER_HEAL, EVAL_RANDOM_BANISH, EVAL_RANDOM_BOUNCE, EVAL_RANDOM_BUFF
EVAL_RANDOM_DESTROY, EVAL_RANDOM_METAMORPHOSE, EVAL_RANDOM_MULTI_DAMAGE, EVAL_RANDOM_MULTI_DAMAGE_MAX, EVAL_RANDOM_MULTI_SELECT_DAMAGE, EVAL_REANIMATE, EVAL_RUSH, EVAL_TARGETING_AND_RANDOM_MULTI_DAMAGE
EVAL_TARGETING_BANISH, EVAL_TARGETING_BOUNCE, EVAL_TARGETING_BUFF, EVAL_TARGETING_DAMAGE, EVAL_TARGETING_DESTROY, EVAL_TARGETING_HEAL, EVAL_TARGETING_METAMORPHOSE, EVAL_TARGETING_MULTI_DESTROY
EVAL_TARGETING_OTHER_DESTROY, EVO_COUNT_IN_GAME, EVO_COUNT_IN_PREVIOUS_TURN, FLOOR, FORCED_EXCHANGE, FUSION_COUNT, FUSION_COUNT_AT_ONCE, FUSION_COUNT_IN_GAME
FUSION_NAME_COUNT, FUSION_NAME_COUNT_IN_GAME, HAND_BANISH_COUNT, HAND_COUNT, HAND_MAX_ATTACK, HAND_NAME_COUNT, HEAL_COUNT, INPLAY_COUNT
INPLAY_LARGEST_LIFE, IS_ATTACK_TARGET, IS_BOTH_CLASS, IS_BURIAL_RITE, IS_CLASH_TARGET, IS_DISCARD_TARGET, IS_ENEMY_AI_ID, IS_HOLDING_BATTLE_SKILL
IS_LEADER_HOLDING_BATTLE_SKILL, IS_NEXT_PLAY, IS_PLAYER_ABILITY_ID, IS_PLAYER_CHARA_ID, IS_REANIMATE, IS_SELECTABLE, IS_SELECTED_TARGET, IS_SKILL_OCCURRED
IS_TRIBE, LEADER_MAX_LIFE, LEAVE_COUNT, LEAVE_NAME_COUNT, LIFE, MEMBER_COUNT, MEMBER_MAX_ATK, NOW_FUSION_COUNT
OWN_DESTROY_COUNT, PLAY_TOKEN_COUNT, PLAYED_COUNT, PLAYED_COUNT_IN_GAME, PLAYED_COUNT_IN_PREVIOUS_TURN, PLAYED_NAME_COUNT, PLAYOUT_ATTACKER_COUNT, POW
RANDOM, RECEIVED_DAMAGE_SUM, RESONANCE_START_COUNT, SKILL_ACTIVATE_COUNT, SKILL_COUNT_FROM_ID, STACK_COUNT, SUMMON_COUNT, TAG_COUNT_FROM_ID
TOTAL_DAMAGE
```

## 6. 调试与制作建议

1. 先让 Deck CSV 只包含正确的卡牌 ID、数量和 `0` 评价，确认对局能进入。
2. Style 每次只增加一种 `Type`，避免多个高优先级策略互相影响。
3. 自定义 Emote 先留空 `VoiceID/TextID`，只测试 `FaceID/MotionID`；确认动作正常后再复制原作资源 ID。
4. 条件先使用常量 `1`，确认标签本身有效后再换成复杂表达式。
5. 表达式异常时首先检查大小写和空格，尤其是括号、比较符和逗号。
6. 未知 Deck Tag 会被丢弃；未知 Style Type 会成为 `None`；未知 Style Category 会变成 `All`。
7. `Mods/AIData/ai_deck.json`、`ai_style.json`、`ai_emote.json` 是 Shadowbus 在原作 AI 数据加载后导出的诊断参考。它们不是可直接选择的 CSV，但适合查找原作参数和资源 ID。

复杂 Deck Tag 往往依赖原作 AI 模拟器中的特定上下文。即使语法能够加载，目标信息不存在时也可能只得到 `0` 评价。优先选择与卡牌真实技能相近的原作 Tag 作为模板，并在实际对局中验证 AI 行为。
