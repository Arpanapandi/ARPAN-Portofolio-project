
const fs = require('fs');
const file = 'c:\\DeliveryControl\\DeliveryControl\\Views\\PreparationWorkflow\\Index.cshtml';
const content = fs.readFileSync(file, 'utf8');
const scriptMatch = content.match(/<script>([\s\S]*?)<\/script>/);
if (scriptMatch) {
    const script = scriptMatch[1];
    const lines = script.split('\n');
    let braces = 0;
    lines.forEach((line, i) => {
        const oldBraces = braces;
        for (let char of line) {
            if (char === '{') braces++;
            if (char === '}') braces--;
        }
        if (braces !== oldBraces) {
            // console.log(`${i + 456}: [${braces}] ${line.trim()}`);
            if (braces < 0) console.log(`NEGATIVE BRACES at line ${i + 456}`);
        }
    });
    console.log(`Final Braces: ${braces}`);
}
