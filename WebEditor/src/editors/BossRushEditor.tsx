import type { BossRushAbility, BossRushBoss, BossRushPackage, IdCatalog } from "../types";
import { classes, uiThemes } from "../data/catalog";
import { newAbility, newBoss } from "../models/defaults";
import { Card, CheckboxField, CollapsibleCard, Field, NumberField, RowActions, Section, SelectField, TextField, moveItem } from "../components/Fields";
import { NumberListEditor, StringListEditor, StringMapEditor, UnknownFieldsEditor } from "../components/Collections";

const packageKeys = ["schema_version", "id", "display_name", "detail_title", "detail_text", "ui_theme", "lobby_background", "default_player_life", "initial_progress", "abilities", "bosses", "hidden_boss"];
const bossKeys = ["name", "enemy_class", "enemy_chara_id", "enemy_emblem_id", "enemy_degree_id", "bossrush_stage_id", "battle3dfield_id", "bgm_id", "enemy_life", "recovery_point", "enemy_skill", "enemy_skills", "enemy_skill_desc", "enemy_ai_id", "player_first_turn", "player_start_pp", "enemy_start_pp", "player_start_field_card_ids", "enemy_start_field_card_ids", "enemy_sleeve_id", "player_emotion_override", "enemy_emotion_override", "special_battle_id", "id_override_in_battle_log", "token_draw_effect_override", "special_token_draw_effect_override", "vs_effect_override", "class_destroy_effect_override", "mission_parameter", "custom_deck_card_ids", "deck_csv", "style_csv", "emote_csv", "logic_level", "use_inner_emote"];

function BossForm({ value, onChange, catalog }: { value: BossRushBoss; onChange: (value: BossRushBoss) => void; catalog: IdCatalog }) {
  const set = <K extends keyof BossRushBoss>(key: K, item: BossRushBoss[K]) => onChange({ ...value, [key]: item });
  const characterOptions = catalog.characters.filter((character) => character.classId === value.enemy_class).map((character) => ({ value: character.id, label: `${character.name} (${character.id})` }));
  if (!characterOptions.some((option) => option.value === value.enemy_chara_id)) characterOptions.unshift({ value: value.enemy_chara_id, label: `自定义角色 ${value.enemy_chara_id}` });
  const aiOptions = catalog.questAi.map((entry) => ({ value: entry.enemyAiId, label: `AI ${entry.enemyAiId} · Deck ${entry.deckId}` }));
  if (!aiOptions.some((option) => option.value === value.enemy_ai_id)) aiOptions.unshift({ value: value.enemy_ai_id, label: `自定义 AI ${value.enemy_ai_id}` });
  return <div className="stack">
    <Section title="Boss 基础信息">
      <div className="field-grid">
        <TextField label="Boss 名称" field="name" value={value.name} onChange={(item) => set("name", item)} />
        <SelectField label="职业" field="enemy_class" value={value.enemy_class} options={classes.slice(1).map((item) => ({ value: item.id, label: `${item.name} (${item.id})` }))} onChange={(item) => set("enemy_class", Number(item))} />
        <SelectField label="角色" field="enemy_chara_id" value={value.enemy_chara_id} options={characterOptions} onChange={(item) => set("enemy_chara_id", Number(item))} />
        <NumberField label="徽章 ID" field="enemy_emblem_id" value={value.enemy_emblem_id} onChange={(item) => set("enemy_emblem_id", item)} />
        <NumberField label="称号 ID" field="enemy_degree_id" value={value.enemy_degree_id} onChange={(item) => set("enemy_degree_id", item)} />
        <NumberField label="BossRush 阶段" field="bossrush_stage_id" value={value.bossrush_stage_id} min={1} onChange={(item) => set("bossrush_stage_id", item)} />
        <NumberField label="3D 场地 ID" field="battle3dfield_id" value={value.battle3dfield_id} min={1} onChange={(item) => set("battle3dfield_id", item)} />
        <TextField label="BGM ID" field="bgm_id" value={value.bgm_id} onChange={(item) => set("bgm_id", item)} />
        <NumberField label="敌方生命" field="enemy_life" value={value.enemy_life} min={1} onChange={(item) => set("enemy_life", item)} />
        <NumberField label="胜利恢复生命" field="recovery_point" value={value.recovery_point} onChange={(item) => set("recovery_point", item)} />
      </div>
    </Section>
    <Section title="技能" description="复用现有特殊战斗技能 DSL">
      <TextField label="兼容单技能" field="enemy_skill" value={value.enemy_skill} multiline wide onChange={(item) => set("enemy_skill", item)} />
      <StringListEditor label="多个敌方技能" field="enemy_skills" value={value.enemy_skills} onChange={(item) => set("enemy_skills", item)} />
      <TextField label="技能说明" field="enemy_skill_desc" value={value.enemy_skill_desc} multiline wide onChange={(item) => set("enemy_skill_desc", item)} />
    </Section>
    <Section title="AI 与牌组">
      <div className="field-grid">
        <SelectField label="官方 AI" field="enemy_ai_id" value={value.enemy_ai_id} options={aiOptions} onChange={(item) => set("enemy_ai_id", Number(item))} />
        <SelectField label="AI 逻辑等级" field="logic_level" value={value.logic_level} options={[{ value: 0, label: "弱 (0)" }, { value: 1, label: "中 (1)" }, { value: 2, label: "强 (2)" }]} onChange={(item) => set("logic_level", Number(item))} />
        <CheckboxField label="使用内置表情" field="use_inner_emote" value={value.use_inner_emote} onChange={(item) => set("use_inner_emote", item)} />
        <TextField label="Deck CSV" field="deck_csv" value={value.deck_csv} onChange={(item) => set("deck_csv", item)} />
        <TextField label="Style CSV" field="style_csv" value={value.style_csv} onChange={(item) => set("style_csv", item)} />
        <TextField label="Emote CSV" field="emote_csv" value={value.emote_csv} onChange={(item) => set("emote_csv", item)} />
      </div>
      <NumberListEditor label="实际敌方牌组" field="custom_deck_card_ids" value={value.custom_deck_card_ids} cardIds onChange={(item) => set("custom_deck_card_ids", item)} hint="推荐 40 张；第一版使用可编辑 ID 列表，后续卡牌数据库将复用此接口。" />
    </Section>
    <Section title="开局规则">
      <div className="field-grid">
        <SelectField label="先后手" field="player_first_turn" value={value.player_first_turn == null ? "random" : String(value.player_first_turn)} options={[{ value: "random", label: "游戏决定" }, { value: "true", label: "玩家先手" }, { value: "false", label: "敌方先手" }]} onChange={(item) => set("player_first_turn", item === "random" ? null : item === "true")} />
        <NumberField label="玩家初始 PP" field="player_start_pp" value={value.player_start_pp} min={0} max={10} onChange={(item) => set("player_start_pp", item)} />
        <NumberField label="敌方初始 PP" field="enemy_start_pp" value={value.enemy_start_pp} min={0} max={10} onChange={(item) => set("enemy_start_pp", item)} />
      </div>
      <NumberListEditor label="玩家开局场面" field="player_start_field_card_ids" value={value.player_start_field_card_ids} cardIds max={5} onChange={(item) => set("player_start_field_card_ids", item)} />
      <NumberListEditor label="敌方开局场面" field="enemy_start_field_card_ids" value={value.enemy_start_field_card_ids} cardIds max={5} onChange={(item) => set("enemy_start_field_card_ids", item)} />
    </Section>
    <Section title="剧情特殊战斗字段" collapsible defaultOpen={false}>
      <div className="field-grid">
        <NumberField label="敌方卡背" field="enemy_sleeve_id" value={value.enemy_sleeve_id} onChange={(item) => set("enemy_sleeve_id", item)} />
        <NumberField label="玩家表情覆盖" field="player_emotion_override" value={value.player_emotion_override} onChange={(item) => set("player_emotion_override", item)} />
        <NumberField label="敌方表情覆盖" field="enemy_emotion_override" value={value.enemy_emotion_override} onChange={(item) => set("enemy_emotion_override", item)} />
        <TextField label="特殊战斗 ID" field="special_battle_id" value={value.special_battle_id} onChange={(item) => set("special_battle_id", item)} />
        <TextField label="战斗日志 ID 替换" field="id_override_in_battle_log" value={value.id_override_in_battle_log} onChange={(item) => set("id_override_in_battle_log", item)} />
        <TextField label="Token 抽取特效" field="token_draw_effect_override" value={value.token_draw_effect_override} onChange={(item) => set("token_draw_effect_override", item)} />
        <TextField label="特殊 Token 特效" field="special_token_draw_effect_override" value={value.special_token_draw_effect_override} onChange={(item) => set("special_token_draw_effect_override", item)} />
        <CheckboxField label="VS 特效覆盖" field="vs_effect_override" value={value.vs_effect_override} onChange={(item) => set("vs_effect_override", item)} />
        <NumberField label="主战者破坏特效" field="class_destroy_effect_override" value={value.class_destroy_effect_override} onChange={(item) => set("class_destroy_effect_override", item)} />
      </div>
      <StringMapEditor label="任务参数" field="mission_parameter" value={value.mission_parameter} valueMultiline onChange={(item) => set("mission_parameter", item)} />
    </Section>
    <UnknownFieldsEditor value={value} knownKeys={bossKeys} onChange={(item) => onChange(item as BossRushBoss)} />
  </div>;
}

export function BossRushEditor({ value, onChange, catalog }: { value: BossRushPackage; onChange: (value: BossRushPackage) => void; catalog: IdCatalog }) {
  const set = <K extends keyof BossRushPackage>(key: K, item: BossRushPackage[K]) => onChange({ ...value, [key]: item });
  return <div className="editor-content">
    <Section title="BossRush 基础设置" description="bossrush.json">
      <div className="field-grid">
        <NumberField label="Schema 版本" field="schema_version" value={value.schema_version} min={5} onChange={(item) => set("schema_version", item)} />
        <TextField label="配置 ID" field="id" value={value.id} onChange={(item) => set("id", item)} />
        <TextField label="显示名称" field="display_name" value={value.display_name} onChange={(item) => set("display_name", item)} />
        <TextField label="详情标题" field="detail_title" value={value.detail_title} onChange={(item) => set("detail_title", item)} />
        <SelectField label="UI 主题" field="ui_theme" value={value.ui_theme} options={uiThemes.map((item) => ({ value: item, label: item }))} onChange={(item) => set("ui_theme", item)} />
        <TextField label="背景资源" field="lobby_background" value={value.lobby_background} onChange={(item) => set("lobby_background", item)} />
        <NumberField label="玩家初始生命" field="default_player_life" value={value.default_player_life} min={1} onChange={(item) => set("default_player_life", item)} />
        <NumberField label="初始 Boss 索引" field="initial_progress" value={value.initial_progress} min={0} onChange={(item) => set("initial_progress", item)} />
        <TextField label="详情正文" field="detail_text" value={value.detail_text} multiline wide onChange={(item) => set("detail_text", item)} />
      </div>
    </Section>

    <Section title="加护池" description={`abilities · ${value.abilities.length} 项`} actions={<button type="button" onClick={() => set("abilities", [...value.abilities, newAbility()])}>新增加护</button>}>
      <div className="stack">{value.abilities.map((ability, index) => <CollapsibleCard key={index} title={`加护 ${index + 1}`} subtitle={`卡牌 ${ability.ability_id}`} defaultOpen={index === 0} actions={<RowActions index={index} count={value.abilities.length} onMove={(from, to) => set("abilities", moveItem(value.abilities, from, to))} onCopy={() => set("abilities", [...value.abilities.slice(0, index + 1), structuredClone(ability), ...value.abilities.slice(index + 1)])} onDelete={() => set("abilities", value.abilities.filter((_, itemIndex) => itemIndex !== index))} />}>
        <div className="field-grid">
          <NumberField label="显示卡牌 ID" field="ability_id" value={ability.ability_id} cardId onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, ability_id: item }; set("abilities", next); }} />
          <CheckboxField label="闪卡" field="is_foil" value={ability.is_foil} onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, is_foil: item }; set("abilities", next); }} />
          <NumberField label="最大生命变化" field="max_life_change" value={ability.max_life_change} onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, max_life_change: item }; set("abilities", next); }} />
          <NumberField label="当前生命变化" field="life_change" value={ability.life_change} onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, life_change: item }; set("abilities", next); }} />
          <TextField label="技能 DSL" field="skill" value={ability.skill} multiline wide onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, skill: item }; set("abilities", next); }} />
          <TextField label="显示说明" field="special_ability_desc" value={ability.special_ability_desc} multiline wide onChange={(item) => { const next = [...value.abilities]; next[index] = { ...ability, special_ability_desc: item }; set("abilities", next); }} />
        </div>
      </CollapsibleCard>)}</div>
    </Section>

    <Section title="主线 Boss" description={`bosses · ${value.bosses.length} 关`} actions={<button type="button" onClick={() => set("bosses", [...value.bosses, newBoss(`第 ${value.bosses.length + 1} 战`)])}>新增 Boss</button>}>
      <div className="stack">{value.bosses.map((boss, index) => <details className="boss-details" key={index} open={index === 0}><summary><span><strong>第 {index + 1} 战 · {boss.name}</strong><small>{classes.find((item) => item.id === boss.enemy_class)?.name} / {boss.enemy_life} 生命</small></span><span className="row-actions" onClick={(event) => event.preventDefault()}><RowActions index={index} count={value.bosses.length} onMove={(from, to) => set("bosses", moveItem(value.bosses, from, to))} onCopy={() => set("bosses", [...value.bosses.slice(0, index + 1), structuredClone(boss), ...value.bosses.slice(index + 1)])} onDelete={() => set("bosses", value.bosses.filter((_, itemIndex) => itemIndex !== index))} /></span></summary><BossForm value={boss} catalog={catalog} onChange={(item) => { const next = [...value.bosses]; next[index] = item; set("bosses", next); }} /></details>)}</div>
    </Section>

    <Section title="隐藏 Boss" description="hidden_boss">
      <CheckboxField label="启用隐藏 Boss" value={value.hidden_boss != null} onChange={(item) => set("hidden_boss", item ? newBoss("隐藏 Boss") : null)} />
      {value.hidden_boss && <BossForm value={value.hidden_boss} catalog={catalog} onChange={(item) => set("hidden_boss", item)} />}
    </Section>
    <UnknownFieldsEditor value={value} knownKeys={packageKeys} onChange={(item) => onChange(item as BossRushPackage)} />
  </div>;
}
