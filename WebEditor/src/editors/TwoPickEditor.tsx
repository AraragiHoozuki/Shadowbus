import type { TwoPickClassRule, TwoPickRoundRule, TwoPickRule } from "../types";
import { classes } from "../data/catalog";
import { Card, CheckboxField, CollapsibleCard, Field, NumberField, NullableNumberField, RowActions, Section, SelectField, TextField, moveItem } from "../components/Fields";
import { NumberListEditor, NumberMapEditor, UnknownFieldsEditor } from "../components/Collections";

const known = ["id", "displayName", "finalDeckSize", "candidateClassCount", "offersPerRound", "cardsPerOffer", "allowDuplicatePicks", "sameCardLimit", "candidateClasses", "classRules", "roundRules", "cardPool", "excludedCards", "cardWeights"];
const emptyClassRule = (): TwoPickClassRule => ({ displayName: null, cardClasses: null, additionalCards: [], description: null });
const emptyRoundRule = (): TwoPickRoundRule => ({ rounds: [], costs: null, rarities: null, cards: null });

export function TwoPickEditor({ value, onChange }: { value: TwoPickRule; onChange: (value: TwoPickRule) => void }) {
  const set = <K extends keyof TwoPickRule>(key: K, item: TwoPickRule[K]) => onChange({ ...value, [key]: item });
  const classEntries = Object.entries(value.classRules);
  return <div className="editor-content">
    <Section title="双选基础规则" description="TwoPick/*.json">
      <div className="field-grid">
        <TextField label="规则 ID" field="id" value={value.id} onChange={(item) => set("id", item)} />
        <TextField label="显示名称" field="displayName" value={value.displayName} onChange={(item) => set("displayName", item)} />
        <NumberField label="最终卡组张数" field="finalDeckSize" value={value.finalDeckSize} min={6} max={200} onChange={(item) => set("finalDeckSize", item)} />
        <NullableNumberField label="同卡上限" field="sameCardLimit" value={value.sameCardLimit} min={1} onChange={(item) => set("sameCardLimit", item)} />
        <NumberField label="候选职业数" field="candidateClassCount" value={value.candidateClassCount} disabled onChange={() => {}} />
        <NumberField label="每轮候选组" field="offersPerRound" value={value.offersPerRound} disabled onChange={() => {}} />
        <NumberField label="每组卡牌数" field="cardsPerOffer" value={value.cardsPerOffer} disabled onChange={() => {}} />
        <CheckboxField label="允许重复抓取" field="allowDuplicatePicks" value={value.allowDuplicatePicks} onChange={(item) => set("allowDuplicatePicks", item)} />
      </div>
      <Field label="候选职业" field="candidateClasses" wide><div className="check-grid">{classes.slice(1).map((item) => <label key={item.id}><input type="checkbox" checked={(value.candidateClasses ?? []).includes(item.id)} onChange={(event) => { const current = value.candidateClasses ?? []; set("candidateClasses", event.target.checked ? [...current, item.id].sort() : current.filter((id) => id !== item.id)); }} />{item.name}</label>)}</div></Field>
    </Section>

    <Section title="职业规则" description="classRules" actions={<button type="button" onClick={() => { const id = classes.slice(1).find((item) => !(String(item.id) in value.classRules))?.id; if (id) set("classRules", { ...value.classRules, [id]: emptyClassRule() }); }}>新增职业规则</button>}>
      <div className="stack">{classEntries.map(([classId, rule], index) => <CollapsibleCard key={classId} title={`${classes.find((item) => item.id === Number(classId))?.name ?? "职业"} (${classId})`} defaultOpen={index === 0} actions={<button type="button" className="danger" onClick={() => set("classRules", Object.fromEntries(classEntries.filter(([key]) => key !== classId)))}>删除</button>}>
        <div className="field-grid"><TextField label="自定义名称" field="displayName" value={rule.displayName ?? ""} onChange={(item) => set("classRules", { ...value.classRules, [classId]: { ...rule, displayName: item || null } })} /><TextField label="介绍" field="description" value={rule.description ?? ""} multiline wide onChange={(item) => set("classRules", { ...value.classRules, [classId]: { ...rule, description: item || null } })} /></div>
        <Field label="混合职业" field="cardClasses" wide><div className="check-grid">{classes.map((item) => <label key={item.id}><input type="checkbox" checked={(rule.cardClasses ?? []).includes(item.id)} onChange={(event) => { const current = rule.cardClasses ?? []; const next = event.target.checked ? [...current, item.id].sort() : current.filter((id) => id !== item.id); set("classRules", { ...value.classRules, [classId]: { ...rule, cardClasses: next } }); }} />{item.name}</label>)}</div></Field>
        <NumberListEditor label="额外卡牌" field="additionalCards" value={rule.additionalCards} cardIds onChange={(item) => set("classRules", { ...value.classRules, [classId]: { ...rule, additionalCards: item } })} />
      </CollapsibleCard>)}</div>
    </Section>

    <Section title="轮次规则" description={`roundRules · 共 ${value.finalDeckSize / 2} 轮`} actions={<button type="button" onClick={() => set("roundRules", [...value.roundRules, emptyRoundRule()])}>新增轮次规则</button>}>
      <div className="stack">{value.roundRules.map((rule, index) => <CollapsibleCard key={index} title={`轮次规则 ${index + 1}`} subtitle={rule.rounds.length ? `第 ${rule.rounds.join(", ")} 轮` : "尚未选择轮次"} defaultOpen={index === 0} actions={<RowActions index={index} count={value.roundRules.length} onMove={(from, to) => set("roundRules", moveItem(value.roundRules, from, to))} onCopy={() => set("roundRules", [...value.roundRules.slice(0, index + 1), structuredClone(rule), ...value.roundRules.slice(index + 1)])} onDelete={() => set("roundRules", value.roundRules.filter((_, itemIndex) => itemIndex !== index))} />}>
        <NumberListEditor label="适用轮次" field="rounds" value={rule.rounds} onChange={(item) => { const next = [...value.roundRules]; next[index] = { ...rule, rounds: item }; set("roundRules", next); }} />
        <NumberListEditor label="允许费用" field="costs" value={rule.costs ?? []} onChange={(item) => { const next = [...value.roundRules]; next[index] = { ...rule, costs: item.length ? item : null }; set("roundRules", next); }} />
        <Field label="允许稀有度" field="rarities" wide><div className="check-grid">{[1, 2, 3, 4].map((rarity) => <label key={rarity}><input type="checkbox" checked={(rule.rarities ?? []).includes(rarity)} onChange={(event) => { const current = rule.rarities ?? []; const rarities = event.target.checked ? [...current, rarity].sort() : current.filter((item) => item !== rarity); const next = [...value.roundRules]; next[index] = { ...rule, rarities: rarities.length ? rarities : null }; set("roundRules", next); }} />{["铜", "银", "金", "虹"][rarity - 1]}</label>)}</div></Field>
        <NumberListEditor label="指定卡池" field="cards" value={rule.cards ?? []} cardIds onChange={(item) => { const next = [...value.roundRules]; next[index] = { ...rule, cards: item.length ? item : null }; set("roundRules", next); }} />
      </CollapsibleCard>)}</div>
    </Section>

    <Section title="全局卡池" description="cardPool / excludedCards / cardWeights">
      <CheckboxField label="使用所有普通非 Token 卡" value={value.cardPool == null} onChange={(item) => set("cardPool", item ? null : [])} />
      {value.cardPool != null && <NumberListEditor label="全局基础卡池" field="cardPool" value={value.cardPool} cardIds onChange={(item) => set("cardPool", item)} />}
      <NumberListEditor label="硬排除卡牌" field="excludedCards" value={value.excludedCards} cardIds onChange={(item) => set("excludedCards", item)} />
      <NumberMapEditor label="卡牌权重" field="cardWeights" value={value.cardWeights} cardIds onChange={(item) => set("cardWeights", item)} />
    </Section>
    <UnknownFieldsEditor value={value} knownKeys={known} onChange={(item) => onChange(item as TwoPickRule)} />
  </div>;
}
