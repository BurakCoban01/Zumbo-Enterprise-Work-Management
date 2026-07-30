import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { validateMobileVisualReview } from './v3-mobile-visual-review-contract.mjs';

const outputDir = resolve(import.meta.dirname, '../../artifacts/ui/v3-qa-002/mobile-matrix');
const matrix = JSON.parse(await readFile(resolve(outputDir, 'mobile-matrix.json'), 'utf8'));
const review = JSON.parse(await readFile(resolve(outputDir, 'visual-review.json'), 'utf8'));
const result = validateMobileVisualReview(matrix, review);

console.log(
  `V3-QA-002 explicit visual review passed: ${result.reviewedCaptures} captures, `
  + `${result.reviewedAdditionalStates} adverse states.`
);
