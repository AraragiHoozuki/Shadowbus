import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import { createPortal } from "react-dom";
import { Alert, Button, Empty, Input, Space, Spin, Tag, Typography, type InputRef } from "antd";
import { BookOutlined, CloseOutlined, CopyOutlined, SearchOutlined } from "@ant-design/icons";
import { cardImageUrl, cardPortalUrl } from "./Fields";
import { cardSummary, type CardCatalog } from "../data/cards";
import { loadCardReference, searchCardReference, type CardReference, type CardReferenceEntry, type CardReferenceMatch } from "../data/cardReference";
import { cardMasterFieldsSnippet, cardSkillBreakdown, cardSkillDsl, type CardSkillGroup } from "../models/cardDsl";
import { copyText } from "../models/clipboard";
import { EVOLUTION_SEPARATOR } from "../models/skills";

/**
 * A floating, always-on-top reference for the game's own cards: search by name,
 * effect text or raw skill field, then copy the result as bracket DSL while
 * writing your own effect.
 *
 * Deliberately not an antd Modal. A modal blocks the form underneath, and its
 * wrapper swallows clicks even with `mask={false}`, which defeats the point of
 * looking something up *while* editing. This is a plain fixed panel portalled to
 * the body, above antd's modal layer (1000) so it stays visible over the skill
 * DSL dialog too. All antd popups are avoided inside it for the same reason:
 * they would render below it.
 */

const PANEL_MIN = { width: 340, height: 280 };
const PANEL_DEFAULT = { width: 460, height: 560 };
/** How many rows the list renders; the header reports anything beyond it. */
const RESULT_LIMIT = 60;

const clamp = (value: number, low: number, high: number) => Math.min(Math.max(value, low), high);

const hitLabels: Record<CardReferenceMatch["hit"], string> = {
  id: "ID",
  name: "卡名",
  text: "效果文",
  skill: "技能字段",
};

/** A copy button that reports the result in place, since a toast would sit under the panel. */
function CopyButton({ label, text, disabled }: { label: string; text: string; disabled?: boolean }) {
  const [state, setState] = useState<"idle" | "done" | "failed">("idle");
  useEffect(() => {
    if (state === "idle") return;
    const timer = window.setTimeout(() => setState("idle"), 1600);
    return () => window.clearTimeout(timer);
  }, [state]);
  return <Button
    size="small"
    icon={<CopyOutlined />}
    disabled={disabled || !text}
    danger={state === "failed"}
    type={state === "done" ? "primary" : "default"}
    onClick={async () => setState(await copyText(text) ? "done" : "failed")}
  >{state === "done" ? "已复制" : state === "failed" ? "复制失败" : label}</Button>;
}

/**
 * One skill's six fields plus whichever presentation columns the card sets, with
 * its own DSL. Copying a single skill is the common case — you usually want one
 * effect out of a card, not every effect it has — so the per-form aggregate below
 * is the extra, not this.
 */
function SkillGroupCard({ group }: { group: CardSkillGroup }) {
  const rows: [string, string][] = [
    ["skill", group.skill],
    ["timing", group.timing],
    ["condition", group.condition],
    ["target", group.target],
    ["option", group.option],
    ["preprocess", group.preprocess],
  ];
  const presentation: [string, string][] = ([
    ["effect_path", group.effectPath],
    ["se_path", group.sePath],
    ["effect_move_type", group.moveType],
    ["engine_type", group.engineType],
    ["effect_time", group.effectTime],
    ["effect_target_type", group.targetType],
  ] as [string, string][]).filter(([, value]) => value && value.toLowerCase() !== "none");
  const dsl = cardSkillDsl([group]);
  const label = `${group.form === "normal" ? "进化前" : "进化后"} 技能 ${group.index}`;
  return <div className="card-ref-skill">
    <div className="card-ref-skill-heading">
      <Tag color={group.form === "normal" ? "blue" : "purple"}>{label}</Tag>
      <CopyButton label="复制此技能" text={dsl} />
    </div>
    <dl className="card-ref-skill-fields">
      {rows.map(([key, value]) => <div className="card-ref-skill-row" key={key}>
        <dt>{key}</dt>
        <dd>{value || <span className="card-ref-muted">（空）</span>}</dd>
      </div>)}
      {presentation.map(([key, value]) => <div className="card-ref-skill-row card-ref-skill-row-extra" key={key}>
        <dt>{key}</dt>
        <dd>{value}</dd>
      </div>)}
    </dl>
    <pre className="card-ref-raw card-ref-skill-dsl">{dsl}</pre>
  </div>;
}

function CardReferenceDetail({ entry, cards }: { entry: CardReferenceEntry; cards: CardCatalog }) {
  const breakdown = useMemo(() => cardSkillBreakdown(entry), [entry]);
  const card = cards.get(entry.id);
  const normalDsl = cardSkillDsl(breakdown.normal);
  const evolvedDsl = cardSkillDsl(breakdown.evolved);
  return <div className="card-ref-detail">
    <div className="card-ref-detail-top">
      <a className="card-ref-art" href={cardPortalUrl(entry.id)} target="_blank" rel="noreferrer" title="在 Shadowverse Portal 中打开">
        <img src={cardImageUrl(entry.id)} alt={`Card ${entry.id}`} loading="lazy" />
      </a>
      <div className="card-ref-detail-meta">
        {card && <Typography.Text type="secondary">{cardSummary(card)}</Typography.Text>}
        {entry.text && <div className="card-ref-text">{entry.text}</div>}
        {entry.evoText && <div className="card-ref-text card-ref-text-evo"><Tag color="purple">进化后</Tag>{entry.evoText}</div>}
        {!entry.text && !entry.evoText && <Typography.Text type="secondary">此卡没有效果文，只有技能字段。</Typography.Text>}
      </div>
    </div>

    {entry.effectCondition && <div className="card-ref-effect-condition">
      <Typography.Text type="secondary">skill_effect_condition</Typography.Text>
      <code>{entry.effectCondition}</code>
    </div>}

    <div className="card-ref-fields">
      <div className="card-ref-fields-heading">
        <Typography.Text strong>六个并行字段</Typography.Text>
        <CopyButton label="复制字段 JSON" text={cardMasterFieldsSnippet(entry)} />
      </div>
      <pre className="card-ref-raw">{Object.entries(breakdown.fields).map(([key, value]) => `${key}: ${value || "（空）"}`).join("\n")}</pre>
      {breakdown.hasEvolution && <Typography.Text type="secondary" className="card-ref-note">
        含 {EVOLUTION_SEPARATOR}：前半段是进化前技能，后半段是进化后技能。DSL 无法表达这个分隔，所以下面按形态分别生成。
      </Typography.Text>}
    </div>

    {breakdown.normal.map((group) => <SkillGroupCard key={`normal-${group.index}`} group={group} />)}
    {breakdown.evolved.map((group) => <SkillGroupCard key={`evolved-${group.index}`} group={group} />)}

    <div className="card-ref-dsl">
      <div className="card-ref-fields-heading">
        <Typography.Text strong>{breakdown.evolved.length ? "进化前 DSL" : "技能 DSL"}<span className="card-ref-muted">（合并 {breakdown.normal.length} 个）</span></Typography.Text>
        <CopyButton label="复制 DSL" text={normalDsl} />
      </div>
      <pre className="card-ref-raw">{normalDsl || "（无进化前技能）"}</pre>
    </div>
    {!!breakdown.evolved.length && <div className="card-ref-dsl">
      <div className="card-ref-fields-heading">
        <Typography.Text strong>进化后 DSL<span className="card-ref-muted">（合并 {breakdown.evolved.length} 个）</span></Typography.Text>
        <CopyButton label="复制进化 DSL" text={evolvedDsl} />
      </div>
      <pre className="card-ref-raw">{evolvedDsl}</pre>
    </div>}
  </div>;
}

function CardReferenceResults({ reference, query, cards }: { reference: CardReference; query: string; cards: CardCatalog }) {
  const [selected, setSelected] = useState<number | null>(null);
  const name = useCallback((cardId: number) => cards.get(cardId)?.name, [cards]);
  const result = useMemo(() => searchCardReference(reference, query, name, RESULT_LIMIT), [reference, query, name]);

  if (!query.trim()) {
    return <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={<span>输入卡名、效果文关键词或技能字段名<br />例如 疾驰、当此从者攻击时、damage、100621020<br /><Typography.Text type="secondary">简体和繁体可以互相匹配，卡表是繁体也能用简体搜</Typography.Text></span>}
    />;
  }
  if (!result.matches.length) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={`没有匹配 “${query.trim()}” 的卡牌`} />;

  return <>
    <div className="card-ref-count">
      <Typography.Text type="secondary">
        {result.total} 张匹配{result.total > result.matches.length ? `，显示前 ${result.matches.length} 张` : ""}
      </Typography.Text>
    </div>
    <div className="card-ref-rows">
      {result.matches.map(({ entry, name: cardName, hit }) => {
        const open = selected === entry.id;
        const summary = entry.text || entry.evoText;
        return <div className={`card-ref-row${open ? " card-ref-row-open" : ""}`} key={entry.id}>
          <button type="button" className="card-ref-row-head" aria-expanded={open} onClick={() => setSelected(open ? null : entry.id)}>
            <span className="card-ref-row-title">
              <span className="card-ref-row-name">{cardName ?? "未知卡牌"}</span>
              <code>#{entry.id}</code>
              {entry.skill.includes(EVOLUTION_SEPARATOR) && <Tag color="purple">进化</Tag>}
              <Tag>{hitLabels[hit]}</Tag>
            </span>
            <span className="card-ref-row-summary">{summary || entry.skill || "（无效果文）"}</span>
          </button>
          {open && <CardReferenceDetail entry={entry} cards={cards} />}
        </div>;
      })}
    </div>
  </>;
}

export function CardReferencePanel({ cards }: { cards: CardCatalog }) {
  const [open, setOpen] = useState(false);
  /** Once opened the panel stays mounted and hidden, so the query and scroll survive closing. */
  const [mounted, setMounted] = useState(false);
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [reference, setReference] = useState<CardReference | null>(null);
  const [loading, setLoading] = useState(false);
  const [placement, setPlacement] = useState<{ left: number; top: number } | null>(null);
  const [size, setSize] = useState(PANEL_DEFAULT);
  const panelRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<InputRef>(null);
  const drag = useRef<{ pointerId: number; mode: "move" | "resize"; startX: number; startY: number; left: number; top: number; width: number; height: number } | null>(null);

  const show = () => {
    setMounted(true);
    setOpen(true);
    if (!reference && !loading) {
      setLoading(true);
      // The generated module is a separate chunk, so this is also the download.
      loadCardReference().then((loaded) => { setReference(loaded); setLoading(false); });
    }
    window.setTimeout(() => searchRef.current?.focus(), 0);
  };

  /**
   * Escape closes the panel from anywhere, not just when focus happens to be
   * inside it. The panel's whole purpose is to sit open while you edit the form
   * underneath, so focus is usually somewhere else and a handler on the panel
   * element alone would never fire. Keys coming from inside a modal or drawer are
   * left alone, so Escape still closes the topmost thing first.
   */
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || event.defaultPrevented) return;
      const target = event.target as Element | null;
      if (target?.closest?.(".ant-modal, .ant-drawer")) return;
      setOpen(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open]);

  /**
   * Starts a move or resize, unless the pointer went down on a control inside the
   * header. Capturing the pointer there retargets the following pointerup — and
   * with it the synthesised click — to the header, and cancelling pointerdown
   * suppresses the compatibility mouse events outright, so the close button's
   * onClick would never fire. Guarded here rather than on the button so any
   * control later added to the header keeps working.
   */
  const beginDrag = (mode: "move" | "resize") => (event: ReactPointerEvent<HTMLElement>) => {
    if (event.button !== 0) return;
    if ((event.target as Element | null)?.closest?.("button, input, a, [role='button']")) return;
    const rect = panelRef.current?.getBoundingClientRect();
    if (!rect) return;
    drag.current = { pointerId: event.pointerId, mode, startX: event.clientX, startY: event.clientY, left: rect.left, top: rect.top, width: rect.width, height: rect.height };
    event.currentTarget.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  };

  const continueDrag = (event: ReactPointerEvent<HTMLElement>) => {
    const state = drag.current;
    if (!state || state.pointerId !== event.pointerId) return;
    const deltaX = event.clientX - state.startX;
    const deltaY = event.clientY - state.startY;
    if (state.mode === "move") {
      // Kept inside the viewport, but only the header has to stay reachable: a
      // panel dragged off screen would be impossible to grab again.
      setPlacement({
        left: clamp(state.left + deltaX, 8 - state.width + 120, window.innerWidth - 120),
        top: clamp(state.top + deltaY, 8, window.innerHeight - 48),
      });
    } else {
      setSize({
        width: clamp(state.width + deltaX, PANEL_MIN.width, window.innerWidth - 16),
        height: clamp(state.height + deltaY, PANEL_MIN.height, window.innerHeight - 16),
      });
    }
  };

  const endDrag = (event: ReactPointerEvent<HTMLElement>) => {
    if (drag.current?.pointerId === event.pointerId) drag.current = null;
  };

  const style = {
    width: size.width,
    height: size.height,
    ...(placement ? { left: placement.left, top: placement.top, right: "auto", bottom: "auto" } : {}),
    ...(open ? {} : { display: "none" }),
  };

  return createPortal(<>
    <button
      type="button"
      className={`card-ref-fab${open ? " card-ref-fab-active" : ""}`}
      aria-label={open ? "关闭卡牌效果参考" : "打开卡牌效果参考"}
      title={open ? "关闭卡牌效果参考" : "打开卡牌效果参考（搜索卡名或效果文，复制为 DSL）"}
      onClick={() => open ? setOpen(false) : show()}
    ><BookOutlined /></button>

    {mounted && <div
      ref={panelRef}
      className="card-ref-panel"
      style={style}
      role="dialog"
      aria-label="卡牌效果参考"
    >
      <div
        className="card-ref-panel-header"
        onPointerDown={beginDrag("move")}
        onPointerMove={continueDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      >
        <Space size={8}>
          <BookOutlined />
          <Typography.Text strong>卡牌效果参考</Typography.Text>
          {reference && !reference.error && <Tag>{reference.entries.length} 张</Tag>}
        </Space>
        <Button type="text" size="small" aria-label="收起参考面板" icon={<CloseOutlined />} onClick={() => setOpen(false)} />
      </div>

      <div className="card-ref-panel-search">
        <Input
          ref={searchRef}
          allowClear
          prefix={<SearchOutlined />}
          placeholder="搜索卡名、效果文或技能字段"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
      </div>

      <div className="card-ref-panel-body">
        {reference?.error && <Alert type="error" showIcon message="参考数据不可用" description={reference.error} />}
        {!reference && <div className="card-ref-loading"><Space direction="vertical" align="center"><Spin /><Typography.Text type="secondary">正在加载内置卡牌技能数据...</Typography.Text></Space></div>}
        {reference && !reference.error && <>
          {!reference.hasEvoText && <Alert
            className="card-ref-alert"
            type="info"
            showIcon
            message="进化专属效果缺少文本"
            description="生成数据时的导出没有 evo_skill_description 列。用当前版本启动一次游戏并重新运行 npm run build:reference 即可补全。"
          />}
          <CardReferenceResults reference={reference} query={deferredQuery} cards={cards} />
        </>}
      </div>

      <div
        className="card-ref-resize"
        aria-hidden
        onPointerDown={beginDrag("resize")}
        onPointerMove={continueDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      />
    </div>}
  </>, document.body);
}
