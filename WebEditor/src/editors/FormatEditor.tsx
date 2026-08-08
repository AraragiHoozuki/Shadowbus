import type { CustomFormat } from "../types";
import { Field, Section, TextField, NullableNumberField } from "../components/Fields";
import { NumberMapEditor, UnknownFieldsEditor } from "../components/Collections";

const known = ["id", "displayName", "deckSizeLimit", "sameCardLimit", "tokenCardTotalLimit", "tokenSameCardLimit", "cardLimits"];

export function FormatEditor({ value, onChange }: { value: CustomFormat; onChange: (value: CustomFormat) => void }) {
  const set = <K extends keyof CustomFormat>(key: K, item: CustomFormat[K]) => onChange({ ...value, [key]: item });
  return <div className="editor-content">
    <Section title="赛制基本设置" description="Format/*.json">
      <div className="field-grid">
        <TextField label="赛制 ID" field="id" value={value.id} onChange={(item) => set("id", item.toLowerCase())} />
        <TextField label="显示名称" field="displayName" value={value.displayName} onChange={(item) => set("displayName", item)} />
        <NullableNumberField label="卡组总张数上限" field="deckSizeLimit" value={value.deckSizeLimit} min={0} onChange={(item) => set("deckSizeLimit", item)} />
        <NullableNumberField label="普通同名卡上限" field="sameCardLimit" value={value.sameCardLimit} min={0} onChange={(item) => set("sameCardLimit", item)} />
        <NullableNumberField label="Token 总数上限" field="tokenCardTotalLimit" value={value.tokenCardTotalLimit} min={0} onChange={(item) => set("tokenCardTotalLimit", item)} />
        <NullableNumberField label="同名 Token 上限" field="tokenSameCardLimit" value={value.tokenSameCardLimit} min={0} onChange={(item) => set("tokenSameCardLimit", item)} />
      </div>
    </Section>
    <NumberMapEditor label="个别卡牌限制" field="cardLimits" value={value.cardLimits} cardIds onChange={(item) => set("cardLimits", item)} />
    <UnknownFieldsEditor value={value} knownKeys={known} onChange={(item) => onChange(item as CustomFormat)} />
  </div>;
}
