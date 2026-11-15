// No Node type declarations are installed in this workspace (the same reason
// `i18n-catalogue.spec.ts` gives). These are the few the journeys use.
declare const process: { env: Record<string, string | undefined> };

declare module 'node:crypto' {
  export function randomBytes(size: number): { toString(encoding: string): string };
}

declare module 'node:fs' {
  export function mkdirSync(path: string, options?: { recursive?: boolean }): void;
  export function rmSync(path: string, options?: { recursive?: boolean; force?: boolean }): void;
  export function readFileSync(path: string, encoding: 'utf8'): string;
}

declare module 'node:path' {
  export function join(...parts: string[]): string;
}
