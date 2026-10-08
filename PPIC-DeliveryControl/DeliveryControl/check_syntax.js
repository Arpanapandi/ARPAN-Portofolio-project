
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const files = [
    path.join(__dirname, 'Views', 'Pulling', 'Index.cshtml'),
    path.join(__dirname, 'Views', 'PreparationWorkflow', 'Index.cshtml'),
    path.join(__dirname, 'Views', 'Home', 'Index.cshtml')
];

files.forEach(file => {
    if (!fs.existsSync(file)) {
        console.log(`File not found: ${file}`);
        return;
    }
    const dirName = path.basename(path.dirname(file));
    console.log(`Checking ${file}...`);
    const content = fs.readFileSync(file, 'utf8');
    const scriptMatches = content.match(/<script>([\s\S]*?)<\/script>/g);

    if (scriptMatches) {
        scriptMatches.forEach((match, index) => {
            const script = match.replace(/<script>|<\/script>/g, '');
            const tempFile = path.join(__dirname, `temp_script_${dirName}_${path.basename(file)}_${index}.js`);
            fs.writeFileSync(tempFile, script);
            try {
                execSync(`node --check "${tempFile}"`);
                console.log(`  Script block ${index}: OK`);
            } catch (err) {
                console.error(`  Script block ${index}: ERROR`);
                console.error(err.stdout.toString() || err.stderr.toString());
            }
        });
    } else {
        console.log('  No script blocks found.');
    }
});
