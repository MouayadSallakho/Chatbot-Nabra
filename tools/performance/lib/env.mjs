// Settings the performance tools cannot run without.
//
// The synthetic accounts' password is never committed. It lives with the local
// stack's evidence, outside the repository, and reaches a tool only through its
// environment. A tool that needs it stops before opening any connection, and
// says which variable to set.

/** The value of a required environment variable, or a clear stop with exit code 2. */
export function requiredEnv(name, purpose) {
  const value = process.env[name];

  if (!value) {
    console.error(`${name} is not set. ${purpose}`);
    process.exit(2);
  }

  return value;
}

/** The password of the synthetic accounts the seed creates and the tools sign in as. */
export function seedPassword() {
  return requiredEnv(
    'SEED_PASSWORD',
    'Set it to the synthetic accounts\' password in this shell first (see tools/performance/README.md). ' +
      'It has no committed default.',
  );
}
