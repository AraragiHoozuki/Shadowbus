# 大模型对战 AI

Shadowbus 可以在自定义练习中用 OpenAI 兼容接口接管对手 AI。默认优先使用 Responses API；只有 `Auto` 模式检测到端点或请求字段明确不兼容时，才单次回退 Chat Completions。
起手换牌仍由原作 AI 处理；玩家侧双 AI 不受影响。

## 配置

首次启动后编辑 BepInEx 配置中的 `[LLMAI]`：

```ini
[LLMAI]
Enabled = true
Endpoint = https://api.openai.com/v1/chat/completions
ResponsesEndpoint =
ChatCompletionsEndpoint =
ApiMode = Auto
ApiKey =
Model =
ReasoningEffort = high
TimeoutSeconds = 12
MaxCandidates = 512
MaxPlanSteps = 12
MaxApiCallsPerTurn = 12
MaxResponseTokens = 768
MaxOutputTokens = 4096
LethalSearchMaxPatterns = 32
LethalSearchBudgetMs = 1000
PromptFile = Mods/AIData/llm_prompt.txt
DebugLogPayloads = false
```

`Endpoint` 保持向后兼容。以 `/responses`、`/chat/completions` 或 `/v1` 结尾时会自动推导两种接口地址；两个专用 Endpoint 可覆盖推导结果。`ApiMode` 接受 `Auto`、`Responses`、`ChatCompletions`。`ReasoningEffort` 接受 `none`、`minimal`、`low`、`medium`、`high`、`xhigh`；留空时不发送。

`MaxOutputTokens` 是 Responses API 的推理与最终 JSON 共享预算；旧 `MaxResponseTokens` 继续用于 Chat Completions。`PromptFile` 不存在时使用程序集内置提示词。API Key、Authorization 与玩家隐藏手牌不会写入日志或请求正文。
只有 `Enabled`、`Endpoint`、`ApiKey` 和 `Model` 都有效时才会接管自定义练习的对手回合。

## 决策协议

Responses 请求使用 `instructions`、`input`、`max_output_tokens`、`reasoning.effort`、`store=false` 和严格 `text.format.json_schema`。Chat 回退继续发送 `reasoning_effort`，模型不支持时会明确失败，不会静默降低推理强度。接口必须返回单个 JSON 对象，不要附加 Markdown：

```json
{
  "state_hash": "请求中的原值",
  "goal": "combo",
  "reason": "先解场，再展开",
  "steps": [
    {
      "step_id": "s1",
      "type": "play",
      "actor": "card:self:104",
      "mode": "accelerate",
      "targets": ["card:opponent:22"]
    },
    {
      "step_id": "s2",
      "type": "turn_end",
      "targets": []
    }
  ]
}
```

`goal` 只接受 `lethal`、`combo`、`tempo`、`defend`、`setup`。动作只接受请求中列出的合法动作；卡牌引用使用运行时 Index，不按卡名匹配。

合法动作会给出 `pp_cost`、`pp_after`、`ep_cost`、`draw_count`、`reveals_hidden_information` 和 `requires_replan_after`。抽牌、融合或其他未知信息边界即使模型遗漏标记，编译器也会在该动作后截断计划并重规划。

## 执行与回退

整份计划先在虚拟战场编译。任一步费用、目标、使用形态或守护限制不合法，整份计划都会被拒绝。`lethal` 计划还必须在虚拟战场确认对方主战者死亡。

真实战场一次只执行一步，等待操作队列和 VFX 结束后比较预测状态。状态偏离时丢弃剩余步骤并带上原目标与已执行步骤重新请求。请求失败、响应非法、候选超过上限或重规划次数耗尽时，原作 AI 从当前真实局面继续本回合。

当真实决策点恰好只有一个合法动作时，控制器会构造并虚拟验证一个本地 `FORCED` 计划，直接执行该动作而不请求 LLM。动作仍使用相同的结算等待、状态校验和未知信息重规划机制。

当前原作 `AIVirtualFusionSimulator` 只更新 `AIVirtualField`，不能可靠地把同一虚拟副本继续转换回 `BattlePlayerPair`。因此融合动作必须作为计划最后一步并设置 `replan_after: true`。

每个真实决策状态都会先运行原作 `AILethalSimulator`。搜索按空出牌模式和原作模式顺序进行，受 `LethalSearchMaxPatterns` 与 `LethalSearchBudgetMs` 限制。候选斩杀仍需通过 Shadowbus 计划编译器复验；只有最终确认对方主战者死亡才直接执行。超时或模拟异常只跳过本地斩杀，不会因此立即交还原 AI。

## 决策日志

LLM AI 使用 BepInEx 的 Message、Info、Warning、Error 和 Debug 级别区分颜色，并用树状结构集中展示目标、理由和已验证动作。例如：

```text
[LLM AI] ┌─ PLAN 2  [TEMPO]
  │  WHY     先抽牌确认选择，再投入剩余 PP
  ├─ 01  PLAY/NORMAL  card:self:4  => a12bc34d...
  └─ 02  TURN END
     2 steps verified
```

`-> MODEL` 和 `-> ACT` 表示请求与实际执行，`+ OK` 表示预测状态匹配；黄色 `REPLAN` 块会说明计划为何被截断，红色 `ORIGINAL AI` 块表示已交还原作 AI。完整请求、响应正文和状态差异仅在 `DebugLogPayloads = true` 且启用 Debug 日志时输出，常规日志不会显示 API Key、Authorization 或原始推理内容。
