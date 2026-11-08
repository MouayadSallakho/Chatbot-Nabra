import { rmSync } from 'node:fs';

/** The saved cookie state lasts as long as the run and no longer. */
export default function globalTeardown(): void {
  const stateDir = process.env['NABRA_E2E_STATE_DIR'];

  if (stateDir) {
    rmSync(stateDir, { recursive: true, force: true });
  }
}
