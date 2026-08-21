/**
 * Clipboard writes for the copy buttons.
 *
 * `navigator.clipboard` needs a secure context and a user gesture; GitHub Pages
 * is HTTPS so it is the normal path, but a denied permission prompt or an
 * insecure origin has to fall back to the old selection trick rather than
 * silently doing nothing.
 */
export async function copyText(text: string): Promise<boolean> {
  if (!text) return false;
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Permission denied or no secure context; the fallback below still works.
  }
  return legacyCopy(text);
}

function legacyCopy(text: string) {
  const area = document.createElement("textarea");
  area.value = text;
  // Off screen but still focusable, which execCommand("copy") requires.
  area.setAttribute("readonly", "");
  area.style.position = "fixed";
  area.style.top = "-1000px";
  area.style.opacity = "0";
  document.body.appendChild(area);
  try {
    area.select();
    return document.execCommand("copy");
  } catch {
    return false;
  } finally {
    area.remove();
  }
}
