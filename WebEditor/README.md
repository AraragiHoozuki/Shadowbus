# Shadowbus 一体化配置编辑器

这是一个纯前端 React 应用，用表单编辑 Shadowbus 的 `AIData`、`BossRush`、
`CardMaster`、`Format` 和 `TwoPick` 配置。应用可以部署到 GitHub Pages，所有文件
都只在浏览器本地处理，不会上传到服务器。

## 使用

推荐使用最新版 Edge 或 Chrome，点击“打开 Mods 目录”，选择游戏根目录下的
`Mods`。首次写入时浏览器会请求目录写权限，之后保存会直接修改原文件。

其他浏览器或不希望授予写权限时，可以点击“导入目录”：选择一个 `Mods` 目录，
在内存副本中编辑，完成后点击“导出 ZIP”。ZIP 包含导入时的完整目录；未编辑的
DLL 等二进制文件按原始字节保留。应用不读取浏览器所选目录以外的文件。

直接写入模式下，每个文件在本次页面会话第一次被覆盖、删除或重命名前，都会备份到：

```text
Mods/.shadowbus-editor-backups/<UTC 时间>/<原相对路径>
```

同一文件在同一会话只备份一次。关闭或刷新页面后重新计为新会话。

## 支持的文件

| 模块 | 扫描路径 | 编辑方式 |
| --- | --- | --- |
| BossRush | `BossRush/<包名>/bossrush.json` | 完整已知字段表单、Boss/加护排序复制、隐藏 Boss、本地 AI 引用 |
| AIData | `AIData/deck|style|emote/*.csv` 与 `BossRush/<包名>/ai/.../*.csv` | Deck 基础列和 Tag 组、Style/Emote 表格 |
| CardMaster | `CardMaster/*.json` | 补丁列表、属性字典、技能并行字段、本地化和数组字段 |
| Format | `Format/*.json` | 所有赛制限制和单卡限制 |
| TwoPick | `TwoPick/*.json` | 职业、轮次、卡池、排除与权重规则 |
| AI 参考 | `AIData/ai_*.json`、BossRush Reference 目录 | 只读搜索 |

已知字段始终显示中文说明和原始 JSON/CSV 字段名。JSON 中未知字段可在每层的
“其他 / 未知字段”编辑器中查看与修改；CSV 的未知列会出现在表格末尾并原样写回。
应用内置当前导出的 418 个敌方角色和 61 个 Quest AI 映射，因此离线部署也有 ID
下拉列表；打开的 Mods 中若有 `BossRush/Reference/enemy_chara_ids.csv` 或
`manifest.json`，同 ID 的本地内容会覆盖内置目录。

BossRush 的复制、重命名和删除都会操作整个配置包（包含 `ai` 子目录）；重命名时会
询问是否同步 JSON 内部 `id`。其他 JSON 配置重命名时同样可选择同步内部 `id`。

## 校验与保存

- 错误会禁用保存，例如非法 ID、BossRush 无主线 Boss、TwoPick 使用原版 UI
  不支持的布局或轮次冲突。
- 警告不会阻止保存，但需要再次确认，例如 Boss 自定义牌组不是 40 张。
- 保存后格式化 JSON；CSV 保留原文件的 CRLF/LF 换行风格和标准引号转义。
- 页面存在未保存修改时，切换文件、目录或离开页面会提示确认。

## 游戏内生效时机

- BossRush：每次点击 BossRush 入口时重新扫描并应用配置。
- CardMaster：重新进入卡组列表时热重载。
- Format：重新进入新建卡组、卡组列表、卡组编辑、选牌组或 P2P 对应界面时重载。
- TwoPick：重新打开 P2P 建房规则选择时读取。
- AIData：自定义练习配置页点击“刷新 CSV”；BossRush 本地 AI 随 BossRush 配置重载。

正在进行的战斗不会被网页编辑器或游戏热重载强行重建。修改战斗数据后应退出当前
战斗并按上面的入口重新进入。

## 本地开发

需要 Node.js 22 或更高版本：

```powershell
cd WebEditor
npm install
npm run dev
```

测试和生产构建：

```powershell
npm test
npm run build
npm run test:e2e:install  # 首次运行浏览器测试时下载 Chromium
npm run test:e2e
```

生产文件输出到 `WebEditor/dist`。Vite 的默认 base 为 `/Shadowbus/`，对应本仓库的
GitHub Pages 项目站点；若部署到其他仓库名，请修改 `vite.config.ts` 的 `base`。

## GitHub Pages

仓库包含 `.github/workflows/pages.yml`。在 GitHub 仓库设置中将 Pages 的 Source
设为 “GitHub Actions”，推送到 `main` 或 `master` 后，工作流会执行测试、构建并
部署 `WebEditor/dist`。工作流不需要密钥，也不会把用户的本地 Mods 内容发送到 GitHub。

## CardMaster 攻击特效

CardMaster 编辑器支持 `attackEffectFields`。每个字段使用 `[普通, 进化]` 两个值：`effectPath`（特效路径）、`se`（音效路径）、`moveType`（移动类型）、`effectEnginType`（`NONE`、`SHURIKEN`、`FLATOUT` 或 `SOLID`）和 `time`（秒）。留空字段会继续使用模板卡牌的攻击特效。

## 当前限制

- 卡牌 ID 当前是可批量粘贴的数字列表，尚未接入完整 CardMaster 搜索数据库。
- 编辑器只验证 Shadowbus 已知的结构约束，不解析卡牌技能 DSL 或 AI 表达式语义。
- File System Access API 的直接目录读写只由 Chromium 系浏览器完整支持；其他浏览器
  使用导入目录和 ZIP 导出。
- `Reference`、BossRush `State`、`selected.txt` 和 AI 导出 JSON 只作为参考或运行状态，
  不由表单修改。
