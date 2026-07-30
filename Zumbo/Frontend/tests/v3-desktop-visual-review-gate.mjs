import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { validateDesktopVisualReview } from './v3-desktop-visual-review-contract.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-qa-001/desktop-matrix');
const matrix = JSON.parse(await readFile(resolve(outputDir, 'desktop-matrix.json'), 'utf8'));
const review = JSON.parse(await readFile(resolve(outputDir, 'visual-review.json'), 'utf8'));
const result = validateDesktopVisualReview(matrix, review);

console.log(
  `V3-QA-001 explicit visual review passed: ${result.reviewedCaptures} captures, `
  + `${result.reviewedAdditionalStates} adverse states.`
);
