import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Vitest runs without globals here, so Testing Library cannot register its own
// afterEach and rendered trees would otherwise pile up across tests in a file.
afterEach(cleanup);

if (typeof window !== "undefined" && !window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
}

if (typeof globalThis !== "undefined" && !globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() { /* jsdom layout is not available */ }
    unobserve() { /* jsdom layout is not available */ }
    disconnect() { /* jsdom layout is not available */ }
  } as typeof ResizeObserver;
}
