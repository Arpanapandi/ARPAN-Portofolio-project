
const fs = require('fs');
const file = 'c:\\DeliveryControl\\DeliveryControl\\Views\\Pulling\\Index.cshtml';
const content = fs.readFileSync(file, 'utf8');
const scriptMatch = content.match(/<script>([\s\S]*?)<\/script>/);
if (scriptMatch) {
    const script = scriptMatch[1];
    let braces = 0;
    let parens = 0;
    for (let i = 0; i < script.length; i++) {
        const c = script[i];
        if (c === '{') braces++;
        else if (c === '}') braces--;
        else if (c === '(') parens++;
        else if (c === ')') parens--;
    }
    console.log(`Final Balance - Braces: ${braces}, Parens: ${parens}`);
}
