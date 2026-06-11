// Conventional Commits, enforced in CI by .github/workflows/commitlint.yml
// (wagoid/commitlint-github-action). See https://www.conventionalcommits.org/.
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      ['feat', 'fix', 'perf', 'refactor', 'docs', 'test', 'build', 'ci', 'chore', 'revert'],
    ],
    'subject-case': [2, 'never', ['upper-case', 'pascal-case', 'start-case']],
    'header-max-length': [2, 'always', 100],
    'body-max-line-length': [0, 'always', Infinity], // allow long footers/links
  },
};
