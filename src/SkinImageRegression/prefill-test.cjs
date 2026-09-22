const vm = require('node:vm');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const script = fs.readFileSync(__dirname + '/prefill-script.js', 'utf8');
class Input {
 constructor() { this.raw=''; this.tracked=''; this.state=''; this.focused=false;
 Object.defineProperty(this,'value',{get:()=>this.raw,set:v=>{this.raw=v;this.tracked=v;}}); }
 get value(){return this.raw;} set value(v){this.raw=v;}
 focus(){this.focused=true;} blur(){this.focused=false;}
 getClientRects(){return [{}];}
 dispatchEvent(e){if(e.type==='input' && this.tracked!==this.raw){this.state=this.raw;this.tracked=this.raw;}}
}
function run(host='auth.riotgames.com',typed='',missing=false) {
 const user=new Input(),pass=new Input(); user.value=typed;
 const inputs = {
  'input[data-testid="input-username"][name="username"][type="text"]': user,
  'input[data-testid="input-password"][name="password"][type="password"]': pass
 };
 const context={location:{protocol:'https:',hostname:host},HTMLInputElement:Input,Event:class{constructor(type){this.type=type}},document:{querySelector:s=>missing?null:inputs[s]??null}};
 return {result:vm.runInNewContext(script,context),user,pass};
}
let r=run();assert.equal(r.result,'filled');assert.equal(r.user.state,'test-user');assert.equal(r.pass.state,'test-password');assert.equal(r.user.focused,false);
r=run('example.com');assert.equal(r.result,'blocked');assert.equal(r.pass.value,'');
r=run('auth.riotgames.com','typing-now');assert.equal(r.result,'editing');assert.equal(r.user.value,'typing-now');assert.equal(r.pass.value,'');
assert.equal(run('auth.riotgames.com','',true).result,'waiting');
console.log('PASS: controlled input state, focus/blur, origin restriction, delayed fields, user-edit preservation.');
r=run('authenticate.riotgames.com');assert.equal(r.result,'filled');assert.equal(r.user.state,'test-user');assert.equal(r.pass.state,'test-password');
for(const host of ['authenticate.riotgames.com.example.com','riotgames.com','example.com']) assert.equal(run(host).result,'blocked');
console.log('PASS: actual Riot redirect host allowed, unrelated and lookalike domains blocked.');
