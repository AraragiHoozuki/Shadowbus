## Skill Timings

### 回合与阶段相关
* `turn_start` (回合开始)
* `self_turn_start` (己方回合开始)
* `op_turn_start` (对手回合开始)
* `self_turn_end` (己方回合结束)
* `op_turn_end` (对手回合结束)
* `when_turn_start_immediate`
* `when_battle_start` (战斗开始时，通常指对局开始)

### 卡牌操作与区域移动
* `when_play` (打出时)
* `when_play_other` (打出其他卡牌时)
* `when_summon` (召唤时)
* `when_summon_other` (召唤其他卡牌时)
* `when_summon_self_and_other`
* `when_draw` (抽牌时)
* `when_draw_other`
* `when_add_to_hand` (加入手牌时)
* `when_add_to_deck` (洗入牌堆时)
* `when_discard` (弃牌时)
* `when_discard_other`
* `when_destroy` (被破坏时)
* `when_destroy_other`
* `when_banish` (被消灭/除外时)
* `when_banish_other`
* `when_leave` (离场时)
* `when_leave_other`
* `when_return` (回手时)
* `when_return_other`
* `when_change_inplay` (进入战场/区域变更)
* `when_change_inplay_immediate`
* `when_change_inplay_selfhand`
* `when_hand_to_not_play` (在手牌中未被打出时)

### 战斗与伤害
* `when_attack` (攻击时)
* `when_before_attack` (代码逻辑中由 `when_attack` 触发 `OnBeforeAttackStart`)
* `when_attack_after` (攻击后)
* `when_attack_self_and_other`
* `when_attack_self_and_other_after`
* `when_fight` (交战时)
* `when_fight_after`
* `when_damage` (受到伤害时)
* `when_damage_self_and_other`
* `when_special_lose` (特殊失败条件达成时)

### 进化与变身
* `when_evolve` (进化时)
* `when_evolve_other`
* `when_evolve_before`
* `when_evolve_self_and_other`
* `when_choice_evolve` (选择进化时)
* `when_fusion` (融合时)
* `when_fusion_other`
* `when_fusioned` (被融合时)
* `when_fusion_metamorphose`
* `when_transform` (虽然代码里没直接写 `when_transform` 字符串，但通常伴随变身逻辑，这里提取的是 `when_change_class_life_...` 等相关)

### 特殊关键词效果
* `when_accelerate` (激奏时)
* `when_accelerate_other`
* `when_crystallize` (结晶时)
* `when_crystallize_other`
* `when_enhance` (爆能强化时)
* `when_necromance` (死灵术发动时)
* `when_burial_rite_other` (葬送其他卡牌时)
* `when_spell_charge` (法术增幅/充能时)
* `when_resonance_start` (共鸣开始时)
* `when_use_white_ritual_stack` (使用白骨圣堂堆叠时 - 特定卡牌逻辑)
* `when_choice_play` (抉择打出时)
* `when_choice_brave` (可能是针对特定“英勇/Brave”机制的选择)

### 状态与数值变化
* `when_buff` (获得增益时)
* `when_buff_self_and_other`
* `when_debuff_self_and_other`
* `when_debuff_include_set_max_life`
* `when_healing` (回复生命时)
* `when_healing_other`
* `when_healing_self_and_other`
* `when_pp_healing` (回复PP时)
* `when_change_pptotal` (PP最大值改变时)
* `when_use_ep_self_and_other` (使用EP时)
* `when_chant_count_gain` (吟唱计数增加时)
* `when_skill_chant_count_gain`
* `when_chant_count_gain_self_and_other`
* `when_change_class_life_inplay`
* `when_change_class_life_selfhand`

### 其他
* `when_return_skill_activate`
* `when_skill_activated` (虽然直接字符串没看到，但有 `when_shortage_deck_win_skill_activate`)
* `when_shortage_deck` (牌堆抽空时)
* `when_shortage_deck_win_skill_activate`
* `when_geton` (载具搭乘时)
* `when_getoff` (载具下来时)
* `when_attach_ability` (获得能力时)
* `none` (无)

**提示：**
`SetupSkillTiming` 方法通过计算字符串的 Hash 值来快速匹配（`ComputeStringHash`），上面的列表是从该方法中所有的 `timing == "字符串"` 比较语句中提取的。