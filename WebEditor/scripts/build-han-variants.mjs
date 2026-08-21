#!/usr/bin/env node
// Regenerates src/data/hanVariants.generated.ts: the traditional -> simplified
// character table that lets the card search match across both scripts.
//
//   npm run build:han
//
// The table comes from Windows' own conversion data via LCMapStringEx with
// LCMAP_SIMPLIFIED_CHINESE, so there is no dependency to install and nothing to
// download — the constraint everywhere else in this editor is that bundled data
// must not be fetched at runtime, and generating it offline keeps that true.
//
// Only one direction is generated, and that is deliberate. Search folds both the
// query and the indexed text to simplified before comparing, so one table makes
// matching work in both directions: 繁 folds to 简 and 简 is already 简, so either
// spelling of a query finds either spelling of a card. The reverse direction could
// not be a table at all — simplified 发 is both 發 and 髮, one to many.
//
// The script asserts the properties the runtime fold relies on:
//
//   * the mapping is 1:1 per character, so folding cannot change a string's length
//     and byte offsets stay usable;
//   * no target character is itself a source, so a single pass is idempotent and
//     folding twice is the same as folding once.
//
// PowerShell is driven with a pure ASCII script that exchanges code points as hex.
// Writing CJK literals into a .ps1 does not survive: PowerShell 5.1 reads a
// BOM-less script as the ANSI code page (CP936 here) and mangles them — the same
// DBCS trap that damaged CardMaster_Default_backup.csv.

import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webEditorRoot = resolve(scriptDirectory, "..");
const outputPath = resolve(webEditorRoot, "src/data/hanVariants.generated.ts");

/**
 * CJK Unified Ideographs. Every Han character in the game's own card names and
 * effect text falls in this block — checked, not assumed — so Extension A and the
 * compatibility ideographs would only add weight.
 */
const BLOCK = { first: 0x4e00, last: 0x9fff };

const POWERSHELL = String.raw`
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class HanFold {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern int LCMapStringEx(string locale, uint flags, string src, int srclen,
    StringBuilder dest, int destlen, IntPtr ver, IntPtr res, IntPtr sort);
  public static string Simplify(string s) {
    var sb = new StringBuilder(s.Length * 2 + 16);
    // 0x02000000 = LCMAP_SIMPLIFIED_CHINESE
    int n = LCMapStringEx("zh-CN", 0x02000000, s, s.Length, sb, sb.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    if (n <= 0) throw new Exception("LCMapStringEx failed: " + Marshal.GetLastWin32Error());
    return sb.ToString(0, n);
  }
}
'@
$builder = New-Object System.Text.StringBuilder
for ($cp = FIRST; $cp -le LAST; $cp++) { [void]$builder.Append([char]$cp) }
$source = $builder.ToString()
$folded = [HanFold]::Simplify($source)
Write-Output ("LENGTHS {0} {1}" -f $source.Length, $folded.Length)
for ($i = 0; $i -lt [Math]::Min($source.Length, $folded.Length); $i++) {
  if ($source[$i] -ne $folded[$i]) {
    Write-Output ("{0:X4} {1:X4}" -f [int]$source[$i], [int]$folded[$i])
  }
}
`;

function fail(message) {
  console.error(`build-han-variants: ${message}`);
  process.exit(1);
}

if (process.platform !== "win32") {
  fail("需要 Windows：字表来自系统的 LCMapStringEx。生成好的 src/data/hanVariants.generated.ts 已入库，其他平台无需运行。");
}

const workDirectory = mkdtempSync(join(tmpdir(), "shadowbus-han-"));
const scriptPath = join(workDirectory, "fold.ps1");
let output;
try {
  writeFileSync(scriptPath, POWERSHELL.replace("FIRST", `0x${BLOCK.first.toString(16)}`).replace("LAST", `0x${BLOCK.last.toString(16)}`), "ascii");
  output = execFileSync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath], { encoding: "ascii", maxBuffer: 16 * 1024 * 1024 });
} catch (reason) {
  fail(`调用 PowerShell 失败：${reason instanceof Error ? reason.message : String(reason)}`);
} finally {
  rmSync(workDirectory, { recursive: true, force: true });
}

const lines = output.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
const lengths = lines.shift() ?? "";
const [, sourceLength, foldedLength] = lengths.split(" ");
const expected = BLOCK.last - BLOCK.first + 1;
if (Number(sourceLength) !== expected || Number(foldedLength) !== expected) {
  fail(`折叠改变了长度（送入 ${sourceLength}，返回 ${foldedLength}，期望 ${expected}），映射不是逐字符 1:1，不能按字符替换。`);
}

const pairs = new Map();
for (const line of lines) {
  const match = /^([0-9A-F]{4,6}) ([0-9A-F]{4,6})$/.exec(line);
  if (!match) fail(`PowerShell 输出了无法识别的行：${line}`);
  pairs.set(Number.parseInt(match[1], 16), Number.parseInt(match[2], 16));
}
if (!pairs.size) fail("没有得到任何映射，系统的简繁转换表可能不可用。");

// A target that is itself a source would mean one pass is not enough and the
// result would depend on iteration order.
const chained = [...pairs].filter(([, to]) => pairs.has(to));
if (chained.length) {
  fail(`${chained.length} 个映射的目标字本身又是源字（例如 ${chained.slice(0, 5).map(([from, to]) => `${String.fromCodePoint(from)}->${String.fromCodePoint(to)}`).join(" ")}），单次折叠不再幂等。`);
}

const sorted = [...pairs.keys()].sort((left, right) => left - right);
const traditional = sorted.map((code) => String.fromCodePoint(code)).join("");
const simplified = sorted.map((code) => String.fromCodePoint(pairs.get(code))).join("");

/** Only `\` and the template delimiters can break out of a template literal. */
const escape = (text) => text.replace(/\\/g, "\\\\").replace(/`/g, "\\`").replace(/\$\{/g, "\\${");

const generated = `// Generated by scripts/build-han-variants.mjs from Windows' LCMapStringEx
// (LCMAP_SIMPLIFIED_CHINESE). Do not edit by hand: run \`npm run build:han\`.
//
// ${pairs.size} of the ${expected} characters in U+${BLOCK.first.toString(16).toUpperCase()}..U+${BLOCK.last.toString(16).toUpperCase()} fold to a different
// character. The two strings are parallel: hanTraditional[i] folds to
// hanSimplified[i]. Verified 1:1 per character, and no target is itself a source,
// so one pass is idempotent.
//
// Only this direction exists. Search folds query and text alike to simplified, so
// one table matches both ways; the reverse is one to many (发 is 發 and 髮) and
// could not be a table.

/** Characters that differ from their simplified form. */
export const hanTraditional = \`${escape(traditional)}\`;

/** Their simplified forms, at the same index. */
export const hanSimplified = \`${escape(simplified)}\`;
`;

writeFileSync(outputPath, generated, "utf8");

const kilobytes = (text) => `${(Buffer.byteLength(text, "utf8") / 1024).toFixed(1)} KB`;
console.log(`build-han-variants: 扫描 U+${BLOCK.first.toString(16).toUpperCase()}..U+${BLOCK.last.toString(16).toUpperCase()}（${expected} 个字）`);
console.log(`  ${pairs.size} 个字需要折叠，1:1 且单次幂等`);
console.log(`  写入 ${relative(process.cwd(), outputPath)}（${kilobytes(generated)}）`);
