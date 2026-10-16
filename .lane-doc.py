import subprocess,re
out=subprocess.run(['git','diff','-U0','--','Runtime','Scenes/MovementComponents.cs'],capture_output=True,text=True,encoding='utf-8').stdout
f=None; n=0
for l in out.split('\n'):
    if l.startswith('+++ b/'): f=l[6:]
    m=re.match(r'@@ -\d+(?:,\d+)? \+(\d+)',l)
    if m: n=int(m.group(1))
    if l.startswith('+') and not l.startswith('+++') and re.match(r'\+\s*public ',l) and 'Step' not in f.split('/')[-1][:6]:
        src=open('../../'+f,encoding='utf-8').read().split('\n')
        prev=src[n-2]
        kind='doc' if prev.strip().startswith('///') or prev.strip().startswith('/// ') else ('cmt' if prev.strip().startswith('//') else 'none')
        print(kind,f,n,l[1:].strip()[:100])
    if l.startswith('+') and not l.startswith('+++'): n+=1
