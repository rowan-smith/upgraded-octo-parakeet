const state={token:null,modules:[],context:null,connections:[],changes:[],change:null,local:null,projects:[],starredProjects:[],selectedConnectionId:null,setup:null,me:null,permissions:[],tab:'overview',route:'/home',expandedFiles:{},selectedJobId:null,runPoll:null,setupStep:0,peopleTab:'members',memberDetailTab:'overview',teamDetailTab:'overview',projectAccessTab:'teams',changesTab:'open',overviewPrFilter:'open',searchHits:[],searchIndex:0};
const DEFAULT_LOCAL_PATH='C:\\Users\\rowan\\RiderProjects\\upgraded-octo-parakeet';
const TOKEN_KEY='forgedeck.token';
const el=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

async function api(path,options={}){
  const headers={...(state.token?{Authorization:`Bearer ${state.token}`}:{}),...(options.headers||{})};
  if(options.body!=null && !headers['Content-Type'])headers['Content-Type']='application/json';
  const response=await fetch(path,{...options,headers});
  if(!response.ok){const data=await response.json().catch(()=>({}));const error=new Error(data.error||data.detail||data.title||`Request failed (${response.status})`);error.kind=data.title;error.status=response.status;throw error}
  return response.status===204?null:response.json();
}

async function boot(){
  try{
    state.setup=await api('/api/setup/status');
    const hashRoute=normalizeRoute(location.hash.slice(1)||'');
    if(hashRoute.startsWith('/invite/')){
      el('app').setAttribute('aria-busy','false');
      return renderInviteAccept(decodeURIComponent(hashRoute.slice('/invite/'.length)));
    }
    if(!state.setup.initialised){
      el('appSidebar').style.display='none';
      document.querySelector('.app-shell')?.classList.add('setup-mode');
      el('app').setAttribute('aria-busy','false');
      state.token=localStorage.getItem(TOKEN_KEY);
      if(state.token){
        // Stale session tokens from prior installs cause "Sign in required" on setup POSTs.
        // Keep the token only when it is a valid bootstrap (or owner) session.
        try{
          await api('/api/setup/session');
          state.setup=await api('/api/setup/status');
        }catch{
          state.token=null;
          localStorage.removeItem(TOKEN_KEY);
        }
      }
      return renderSetup();
    }
    state.token=localStorage.getItem(TOKEN_KEY);
    if(!state.token){
      try{
        const login=await api('/api/auth/login',{method:'POST',body:JSON.stringify({email:'maya@forgedeck.dev',password:'demo'})});
        state.token=login.token;localStorage.setItem(TOKEN_KEY,state.token);
      }catch{
        el('appSidebar').style.display='none';
        document.querySelector('.app-shell')?.classList.add('setup-mode');
        el('app').setAttribute('aria-busy','false');
        return renderLogin();
      }
    }
    await loadWorkspace();
  }catch(error){
    if(error.status===401){localStorage.removeItem(TOKEN_KEY);state.token=null;return renderLogin()}
    renderError(error);
  }
}

function applyTheme(theme){
  const resolved=theme==='system'
    ?(window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light')
    :(theme==='dark'?'dark':'light');
  document.documentElement.dataset.theme=resolved;
  document.documentElement.style.colorScheme=resolved;
  state.theme=theme||'system';
}

async function setThemePreference(theme){
  applyTheme(theme);
  try{
    const profile=await api('/api/users/me/preferences',{method:'PATCH',body:JSON.stringify({theme})});
    if(state.me)state.me.profile=profile;
    showToast(`Theme set to ${theme}`);
  }catch(error){showToast(error.message,true)}
}

async function loadWorkspace(){
  el('appSidebar').style.display='';
  document.querySelector('.app-shell')?.classList.remove('setup-mode');
  const [platform,context,connections,local,projects,me,starred]=await Promise.all([
    api('/api/platform/modules'),api('/api/core/context'),api('/api/source/repositories'),
    api('/api/projects/current/local-repository'),api('/api/projects'),api('/api/users/me'),
    api('/api/users/me/starred-projects').catch(()=>[])
  ]);
  state.modules=platform.modules;state.context=context;state.connections=connections;state.local=local;state.projects=projects;state.me=me;state.starredProjects=starred||[];
  state.permissions=me?.permissions||[];
  applyTheme(me?.profile?.theme||'system');
  if(!state.selectedConnectionId||!state.connections.some(c=>c.id===state.selectedConnectionId))
    state.selectedConnectionId=state.connections[0]?.id||null;
  if(hasModule('review'))state.changes=await api('/api/review/changes');
  paintShell();renderNavigation();bindShell();
  const route=location.hash.slice(1)||'/home';
  await navigate(route,false);
  el('app').setAttribute('aria-busy','false');
  setInterval(refreshActiveChange,60000);
}

function paintShell(){
  const org=state.context?.organisation;const project=state.context?.project;
  const profile=state.me?.profile;const user=state.me?.user;
  el('userName').textContent=profile?.displayName||user?.username||'User';
  el('userHandle').textContent=`@${user?.username||'user'}`;
  el('userAvatar').textContent=initials(profile?.displayName||user?.username||'?');
  document.title=`ForgeDeck · ${project?.name||org?.name||'Workspace'}`;
  updateContextChrome();
}

function updateContextChrome(){
  const btn=el('contextButton');
  if(!btn)return;
  const org=state.context?.organisation;
  const project=state.context?.project;
  const orgRoute=isOrgRoute(state.route);
  const showSwitcher=!orgRoute;
  el('orgLabel').textContent=org?.name||'Organisation';
  el('projectLabel').textContent=showSwitcher?(project?.name||'Project'):(org?.name||'Organisation');
  el('projectAvatar').textContent=((showSwitcher?project?.name:org?.name)||'P')[0].toUpperCase();
  btn.hidden=!showSwitcher;
  btn.toggleAttribute('disabled',!showSwitcher);
  btn.setAttribute('aria-disabled',showSwitcher?'false':'true');
  const chevron=btn.querySelector('.chevron');
  if(chevron)chevron.hidden=!showSwitcher;
}

function hasModule(id){return state.modules.some(module=>module.id===id&&module.enabled!==false)}
function can(permission){
  if(!permission)return false;
  const needle=String(permission).toLowerCase();
  return (state.permissions||[]).some(p=>String(p).toLowerCase()===needle);
}
function canAny(...permissions){return permissions.some(can)}
function canCreateProject(){return can('projects.create')||can('team.projects.create')}
function permissionButton(label,attrs,permission,options={}){
  if(permission && !can(permission)){
    if(options.hide)return '';
    return `<button class="button" disabled title="Requires ${esc(permission)}">${esc(label)}</button>`;
  }
  return `<button class="button ${options.primary?'primary':''} ${options.danger?'danger':''} ${options.text?'text':''}" ${attrs}>${esc(label)}</button>`;
}
async function refreshPermissions(){
  try{
    const effective=await api('/api/access/effective');
    state.permissions=effective.permissions||[];
    if(state.me)state.me.permissions=state.permissions;
  }catch{/* keep cached */}
}
function closePermissionDrawer(){
  const drawer=el('permissionDrawer');
  if(!drawer)return;
  drawer.hidden=true;
  drawer.setAttribute('aria-hidden','true');
}
async function openPermissionExplain(permission,scopeType,scopeId,userId){
  const params=new URLSearchParams({permission});
  if(scopeType)params.set('scopeType',scopeType);
  if(scopeId)params.set('scopeId',scopeId);
  if(userId)params.set('userId',userId);
  const drawer=el('permissionDrawer');
  const body=el('permissionDrawerBody');
  const title=el('permissionDrawerTitle');
  if(!drawer||!body||!title)return;
  title.textContent=`Why ${permission}?`;
  body.innerHTML='<p class="description">Loading explanation…</p>';
  drawer.hidden=false;
  drawer.setAttribute('aria-hidden','false');
  el('permissionDrawerClose').onclick=closePermissionDrawer;
  drawer.onclick=e=>{if(e.target===drawer)closePermissionDrawer()};
  try{
    const explanation=await api(`/api/access/explain?${params}`);
    const sources=(explanation.sources||[]).map(source=>{
      const path=[source.teamName,source.roleName||source.roleSlug,source.kind==='direct_grant'?'Direct grant':null,source.projectName]
        .filter(Boolean).join(' → ');
      return `<li><strong>${esc(source.kind||'source')}</strong>${path?` · ${esc(path)}`:''}${source.grantedAt?` · ${esc(new Date(source.grantedAt).toLocaleString())}`:''}</li>`;
    }).join('')||'<li class="description">No granting sources.</li>';
    body.innerHTML=`<p class="description">${explanation.granted?'Granted':'Not granted'} in ${esc(explanation.scopeType||'Organisation')}${explanation.scopeId?` · ${esc(explanation.scopeId)}`:''}</p>
      <ul class="permission-source-list">${sources}</ul>`;
  }catch(error){
    body.innerHTML=`<p class="description">${esc(error.message)}</p>`;
    showToast(error.message,true);
  }
}
function editionLabel(value){return value==='Commercial'?'Enterprise':(value||'Community')}
function licenceModeLabel(value){return value==='Commercial'?'Enterprise':(value||'None')}
async function reloadComposition(){
  const platform=await api('/api/platform/modules');
  state.modules=platform.modules||[];
  renderNavigation();
}
function source(){return state.connections.find(c=>c.id===state.selectedConnectionId)||state.connections[0]||null}
function isMultiRepo(){return state.context?.project?.repositoryMode==='MultiRepository'&&(state.connections||[]).length>1}
function isOrgRoute(route){
  const r=normalizeRoute(route);
  return r==='/home'||r==='/projects'||r==='/people'||r.startsWith('/people/')||r==='/profile'
    ||r.startsWith('/organisation/')||r.startsWith('/invite/');
}
function actor(){return state.context?.user?.name||state.me?.profile?.displayName||'User'}
function crumbs(value){el('breadcrumbs').innerHTML=value}
function projectCrumb(...parts){
  const name=esc(state.context?.project?.name||'Project');
  return [name,...parts].join(' <span>/</span> ');
}
function orgCrumb(...parts){
  const name=esc(state.context?.organisation?.name||'Organisation');
  return [name,...parts].join(' <span>/</span> ');
}
function repoPickerHtml(){
  if(!isMultiRepo())return '';
  return `<label class="repo-picker"><span>Repository</span><select class="field compact" id="repoPicker" aria-label="Repository">${
    state.connections.map(c=>`<option value="${esc(c.id)}" ${c.id===source()?.id?'selected':''}>${esc(c.repositoryId.owner)}/${esc(c.repositoryId.name)}</option>`).join('')
  }</select></label>`;
}
function bindRepoPicker(rerender){
  const picker=el('repoPicker');
  if(!picker)return;
  picker.onchange=()=>{state.selectedConnectionId=picker.value;rerender()};
}
function statusClass(value){return String(value).toLowerCase().replace(/\s+/g,'-')}
function showToast(message,bad=false){const toast=el('toast');toast.textContent=message;toast.classList.toggle('error',bad);toast.classList.add('show');setTimeout(()=>toast.classList.remove('show'),2800)}
function renderError(error){
  const settingsRoute=isOrgRoute(state.route)?'/organisation/settings':'/settings';
  el('content').innerHTML=`<div class="empty"><h2>${esc(error.kind||'Workspace unavailable')}</h2><p>${esc(error.message)}</p><button class="button" data-route="${settingsRoute}">Open settings</button></div>`;
}
function initials(name){return String(name||'?').split(/\s+/).map(part=>part[0]).join('').slice(0,2).toUpperCase()}
function slugify(value){
  return String(value||'').trim().toLowerCase()
    .replace(/[^a-z0-9]+/g,'-')
    .replace(/-+/g,'-')
    .replace(/^-|-$/g,'');
}
function wireAutoSlug(nameId,slugId){
  const nameInput=el(nameId);const slugInput=el(slugId);
  if(!nameInput||!slugInput)return;
  let locked=slugInput.value!==''&&slugInput.value!==slugify(nameInput.value);
  slugInput.addEventListener('input',()=>{locked=slugInput.value.trim()!==''});
  nameInput.addEventListener('input',()=>{
    if(locked)return;
    slugInput.value=slugify(nameInput.value);
  });
  if(!locked)slugInput.value=slugify(nameInput.value);
}

function renderLogin(){
  el('appSidebar').style.display='none';
  document.querySelector('.app-shell')?.classList.add('setup-mode');
  crumbs('Sign in');
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <h1>Sign in</h1>
    <p class="description">Access ${esc(state.setup?.organisationName||'your organisation')}.</p>
    <label class="form-label">Email</label><input class="field" id="loginEmail" type="email" value="maya@forgedeck.dev">
    <label class="form-label">Password</label><input class="field" id="loginPassword" type="password" value="demo">
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="loginSubmit">Sign in</button></div>
  </div></div>`;
  el('loginSubmit').onclick=async()=>{
    try{
      const login=await api('/api/auth/login',{method:'POST',body:JSON.stringify({email:el('loginEmail').value,password:el('loginPassword').value})});
      state.token=login.token;localStorage.setItem(TOKEN_KEY,state.token);await loadWorkspace();
    }catch(error){showToast(error.message,true)}
  };
}

function setupProgressHtml(){
  const steps=state.setup?.steps||[];
  if(!steps.length)return '';
  return `<ol class="setup-progress">${steps.map(step=>{
    const cls=step.complete?'done':step.current?'current':step.locked?'locked':'';
    const mark=step.complete?'✓':step.current?'●':'○';
    return `<li class="${cls}"><span>${mark}</span>${esc(step.title)}</li>`;
  }).join('')}</ol>`;
}

function setupShell(title, body, {orgBrand=true}={}){
  const org=state.setup?.organisationName;
  crumbs(org?`${esc(org)} <span>/</span> Setup`:'Setup');
  return `<div class="setup-layout">
    <aside class="setup-rail">
      <p class="eyebrow"><span class="repo-mark">ForgeDeck</span></p>
      ${orgBrand&&org?`<h2 class="setup-org-brand">${esc(org)}</h2>`:''}
      <h1>Setup</h1>
      ${setupProgressHtml()}
    </aside>
    <div class="setup-shell"><div class="setup-card">${title}${body}</div></div>
  </div>`;
}

async function refreshSetup(){
  state.setup=await api('/api/setup/status');
  return state.setup;
}

function currentSetupStepId(){
  const s=state.setup;
  if(!s?.initialised && !s?.hasOrganisation) return state.token?'organisation':'bootstrap';
  if(!s?.hasLicence) return 'licence';
  if(!s?.hasModules) return 'modules';
  if(!s?.hasOwner) return 'owner';
  return 'finish';
}

function renderSetup(){
  const step=currentSetupStepId();
  if(step==='bootstrap') return renderBootstrapLogin();
  if(step==='organisation') return renderSetupOrganisation();
  if(step==='licence') return renderSetupLicence();
  if(step==='modules') return renderSetupModules();
  if(step==='owner') return renderSetupOwner();
  return renderSetupFinish();
}

function renderBootstrapLogin(){
  crumbs('Initial Setup');
  const warn=state.setup?.developmentBootstrapWarning
    ?`<div class="setup-warning" id="bootstrapWarning">Development credentials are active. Complete setup to secure this installation.</div>`:'';
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <p class="eyebrow"><span class="repo-mark">ForgeDeck</span></p>
    <h1>Initial Setup</h1>
    <p class="description">Sign in with the installation credentials to configure this server.</p>
    ${warn}
    <label class="form-label">Username</label><input class="field" id="bootstrapUser" value="admin" autocomplete="username">
    <label class="form-label">Password</label><input class="field" id="bootstrapPass" type="password" value="admin" autocomplete="current-password">
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="bootstrapSignIn">Sign In</button></div>
  </div></div>`;
  el('bootstrapSignIn').onclick=async()=>{
    try{
      const result=await api('/api/setup/bootstrap-login',{method:'POST',body:JSON.stringify({
        username:el('bootstrapUser').value,password:el('bootstrapPass').value
      })});
      state.token=result.token;localStorage.setItem(TOKEN_KEY,state.token);
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
}

function renderSetupOrganisation(){
  el('content').innerHTML=setupShell(`<h1>Set up your organisation</h1>
    <p class="description">This installation represents exactly one organisation.</p>`, `
    <label class="form-label">Organisation Name</label><input class="field" id="setupOrgName" value="Northstar Labs">
    <label class="form-label">Description</label><input class="field" id="setupOrgDesc" placeholder="Engineering and automation">
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="setupOrgContinue">Continue</button></div>
  `,{orgBrand:false});
  el('setupOrgContinue').onclick=async()=>{
    try{
      await api('/api/setup/organisation',{method:'POST',body:JSON.stringify({
        name:el('setupOrgName').value,description:el('setupOrgDesc').value||null
      })});
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
}

function renderSetupLicence(){
  el('content').innerHTML=setupShell(`<h1>Choose your licence</h1>
    <p class="description">Choose how this installation will be licensed. Community is always available.</p>`, `
    <div class="licence-choice-grid">
      <article class="licence-choice">
        <h2>Community</h2>
        <p>Open-source features. No licence required.</p>
        <button class="button primary" id="setupUseCommunity">Use Community</button>
      </article>
      <article class="licence-choice">
        <h2>Enterprise</h2>
        <p>Unlock Enterprise module capabilities for this installation with a signed licence file.</p>
        <label class="form-label" for="setupLicenceFile">Licence file</label>
        <input class="field" id="setupLicenceFile" type="file" accept=".json,application/json">
        <input type="hidden" id="setupLicencePayload" value="">
        <p class="description" id="setupLicenceFileName" style="margin-top:8px">Choose a signed <code>.json</code> licence file.</p>
        <div class="modal-actions" style="margin-top:12px">
          <button class="button" id="setupValidateLicence">Upload Enterprise Licence</button>
        </div>
      </article>
    </div>
  `);
  wireLicenceFileInput('setupLicenceFile','setupLicencePayload','setupLicenceFileName');
  el('setupUseCommunity').onclick=async()=>{
    try{
      await api('/api/setup/licence/community',{method:'POST',body:'{}'});
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
  el('setupValidateLicence').onclick=async()=>{
    try{
      const payload=el('setupLicencePayload').value;
      if(!payload.trim())return showToast('Choose a licence file first',true);
      await api('/api/setup/licence/commercial',{method:'POST',body:JSON.stringify({payload})});
      await refreshSetup();
      showToast('Enterprise licence active');
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
}

function wireLicenceFileInput(fileId,payloadId,labelId){
  const fileInput=el(fileId);
  const payload=el(payloadId);
  const label=labelId?el(labelId):null;
  if(!fileInput||!payload)return;
  fileInput.onchange=async()=>{
    const file=fileInput.files?.[0];
    const zone=el('licenceDropzone');
    if(!file){
      payload.value='';
      if(label)label.textContent='Drop a signed licence file here';
      zone?.classList.remove('has-file');
      return;
    }
    try{
      payload.value=await file.text();
      if(label)label.textContent=file.name;
      zone?.classList.add('has-file');
    }catch{
      payload.value='';
      if(label)label.textContent='Could not read that file.';
      zone?.classList.remove('has-file');
      showToast('Could not read licence file',true);
    }
  };
}

function renderSetupOwner(){
  el('content').innerHTML=setupShell(`<h1>Create Owner Account</h1>
    <p class="description">The first permanent user becomes the Organisation Owner. Bootstrap access ends after this step.</p>`, `
    <label class="form-label">Display Name</label><input class="field" id="setupDisplayName" value="Rowan Smith">
    <label class="form-label">Username</label><input class="field" id="setupUsername" value="rowan">
    <label class="form-label">Email</label><input class="field" id="setupEmail" type="email" value="rowan@example.com">
    <label class="form-label">Password</label><input class="field" id="setupPassword" type="password" value="password123">
    <label class="form-label">Confirm Password</label><input class="field" id="setupPassword2" type="password" value="password123">
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="setupCreateOwner">Create Owner</button></div>
  `);
  el('setupCreateOwner').onclick=async()=>{
    const password=el('setupPassword').value;
    if(password!==el('setupPassword2').value)return showToast('Passwords do not match',true);
    try{
      const result=await api('/api/setup/owner',{method:'POST',body:JSON.stringify({
        displayName:el('setupDisplayName').value,
        username:el('setupUsername').value,
        email:el('setupEmail').value,
        password
      })});
      state.token=result.token;localStorage.setItem(TOKEN_KEY,state.token);
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
}

async function renderSetupModules(){
  let catalogue=[];
  try{catalogue=await api('/api/setup/modules')}catch{catalogue=[]}
  const defaults=new Set(['forgedeck.code','forgedeck.review']);
  el('content').innerHTML=setupShell(`<h1>Choose capabilities</h1>
    <p class="description">Install bundled modules now, or skip and add them later from Organisation Settings. Module configuration happens inside each project.</p>`, `
    <div class="module-choice-grid" id="setupModuleGrid">
      ${catalogue.map(m=>`
        <article class="module-choice-card">
          <h2>${esc(m.name)}</h2>
          <p>${esc(m.summary||'')}</p>
          <label class="choice-row"><input type="checkbox" data-extension-id="${esc(m.extensionId)}" ${defaults.has(m.extensionId)?'checked':''}> Install</label>
        </article>`).join('')||'<p class="description">No bundled modules are available in this build.</p>'}
    </div>
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupSkipModules">Skip for now</button>
      <button class="button primary" id="setupInstallModules">Continue</button>
    </div>
  `);
  const submit=async(skip)=>{
    try{
      const ids=skip?[]:[...document.querySelectorAll('#setupModuleGrid input[data-extension-id]:checked')].map(i=>i.dataset.extensionId);
      await api('/api/setup/modules',{method:'POST',body:JSON.stringify({extensionIds:ids,skip})});
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
  el('setupSkipModules').onclick=()=>submit(true);
  el('setupInstallModules').onclick=()=>submit(false);
}

function renderSetupFinish(){
  const s=state.setup||{};
  el('content').innerHTML=setupShell(`<h1>${esc(s.organisationName||'ForgeDeck')} is ready</h1>
    <p class="description">Installation onboarding is complete. Create a project when you are ready — module settings live in each project.</p>`, `
    <div class="setup-summary">
      <div><span>Organisation</span><strong>${esc(s.organisationName||'—')}</strong></div>
      <div><span>Edition</span><strong>${esc(licenceModeLabel(s.licenceMode)||'Community')}</strong></div>
      <div><span>Owner</span><strong>Created</strong></div>
    </div>
    <div class="modal-actions" style="margin-top:22px">
      <button class="button primary" id="setupOpenWorkspace">Open ForgeDeck</button>
    </div>
  `);
  el('setupOpenWorkspace').onclick=async()=>{
    try{await api('/api/setup/complete',{method:'POST',body:'{}'})}catch{}
    await finishSetup();
  };
}

async function finishSetup(){
  await loadWorkspace();
  showToast('You are ready');
  navigate('/home');
}

function openStatuses(){return state.changes.filter(change=>!['Merged','Closed'].includes(change.status))}
function hasOpenChangeForBranch(branch){return openStatuses().some(change=>change.sourceBranch===branch)}

let hashNavigating=false;

function normalizeRoute(route){
  let value=String(route??'').trim();
  if(value.startsWith('#'))value=value.slice(1);
  if(!value)return '/home';
  if(!value.startsWith('/'))value=`/${value}`;
  if(value==='/organisation/licensing'||value==='/licensing')return '/organisation/settings/license';
  if(value==='/modules')return '/organisation/settings/modules';
  if(value==='/connectors')return '/organisation/settings/connectors';
  if(value==='/audit')return '/organisation/settings/audit';
  if(value==='/organisation/settings')return '/organisation/settings/general';
  if(value==='/settings')return '/settings/general';
  if(value==='/runners')return '/organisation/settings/build';
  return value;
}

function routeMatchesNav(route,navRoute){
  if(!navRoute)return false;
  if(route===navRoute)return true;
  if(navRoute==='/overview')return false;
  // Prefer segment boundaries so /runs does not match /runners.
  return route.startsWith(`${navRoute}/`)||route.startsWith(`${navRoute}?`);
}

function setActiveNav(route){
  document.querySelectorAll('.nav-item[data-route]').forEach(item=>{
    const navRoute=item.dataset.route;
    let active=routeMatchesNav(route,navRoute);
    if(navRoute==='/organisation/settings'&&route.startsWith('/organisation/settings'))active=true;
    if(navRoute==='/settings'&&route.startsWith('/settings'))active=true;
    item.classList.toggle('active',active);
  });
  document.querySelectorAll('.settings-nav-item[data-route]').forEach(item=>{
    item.classList.toggle('active',route===item.dataset.route||route.startsWith(item.dataset.route+'/'));
  });
}

let shellBound=false;
function bindShell(){
  if(!shellBound){
    document.addEventListener('click',event=>{
      const link=event.target.closest('[data-route]');
      if(!link)return;
      const target=normalizeRoute(link.getAttribute('data-route')||link.dataset.route);
      if(!target)return;
      event.preventDefault();
      navigate(target);
      document.querySelector('.sidebar')?.classList.remove('open');
    });
    shellBound=true;
  }
  el('mobileMenu').onclick=()=>document.querySelector('.sidebar').classList.toggle('open');
  bindGlobalSearch();
  el('contextButton').onclick=()=>{
    if(isOrgRoute(state.route))return;
    openProjectSwitcher();
  };
  bindUserMenu();
}

function bindGlobalSearch(){
  const input=el('globalSearch');
  const panel=el('searchPanel');
  if(!input||input.dataset.bound)return;
  input.dataset.bound='1';
  let debounce=null;
  const runSearch=async()=>{
    const q=input.value.trim();
    try{
      const payload=await api(`/api/search?q=${encodeURIComponent(q)}&limit=12`);
      state.searchHits=payload.hits||[];
      state.searchIndex=0;
      renderSearchPanel();
    }catch(error){
      state.searchHits=[];
      renderSearchPanel(error.message);
    }
  };
  input.addEventListener('focus',()=>{runSearch()});
  input.addEventListener('input',()=>{
    clearTimeout(debounce);
    debounce=setTimeout(runSearch,120);
  });
  input.addEventListener('keydown',async e=>{
    if(e.key==='ArrowDown'){
      e.preventDefault();
      state.searchIndex=Math.min((state.searchHits.length||1)-1,(state.searchIndex||0)+1);
      renderSearchPanel();
      return;
    }
    if(e.key==='ArrowUp'){
      e.preventDefault();
      state.searchIndex=Math.max(0,(state.searchIndex||0)-1);
      renderSearchPanel();
      return;
    }
    if(e.key==='Enter'){
      e.preventDefault();
      const hit=state.searchHits[state.searchIndex||0];
      if(hit)await activateSearchHit(hit);
      return;
    }
    if(e.key==='Escape'){
      closeSearchPanel();
      input.blur();
    }
  });
  document.addEventListener('keydown',e=>{
    if((e.ctrlKey||e.metaKey)&&String(e.key).toLowerCase()==='k'){
      e.preventDefault();
      input.focus();
      input.select();
    }
  });
  document.addEventListener('click',e=>{
    if(!e.target.closest('.global-search'))closeSearchPanel();
  });
  if(panel){
    panel.addEventListener('mousedown',e=>e.preventDefault());
  }
}

function closeSearchPanel(){
  const panel=el('searchPanel');
  if(panel)panel.hidden=true;
}

function renderSearchPanel(errorMessage){
  const panel=el('searchPanel');
  if(!panel)return;
  const hits=state.searchHits||[];
  if(errorMessage){
    panel.hidden=false;
    panel.innerHTML=`<div class="search-empty">${esc(errorMessage)}</div>`;
    return;
  }
  if(!hits.length){
    panel.hidden=false;
    panel.innerHTML=`<div class="search-empty">No matches. Try a project, person, PR, or pipeline name.</div>`;
    return;
  }
  panel.hidden=false;
  panel.innerHTML=hits.map((hit,index)=>`
    <button type="button" class="search-hit ${index===state.searchIndex?'active':''}" data-search-index="${index}" role="option" aria-selected="${index===state.searchIndex?'true':'false'}">
      <span class="search-hit-type">${esc(hit.type||'result')}</span>
      <span class="search-hit-copy"><strong>${esc(hit.label)}</strong>${hit.subtitle?`<small>${esc(hit.subtitle)}</small>`:''}</span>
    </button>`).join('');
  panel.querySelectorAll('[data-search-index]').forEach(btn=>{
    btn.onclick=async()=>{
      const hit=hits[Number(btn.dataset.searchIndex)];
      if(hit)await activateSearchHit(hit);
    };
  });
}

async function activateSearchHit(hit){
  closeSearchPanel();
  const input=el('globalSearch');
  if(input)input.value='';
  if(hit.type==='project'&&hit.id){
    await openProject(hit.id);
    return;
  }
  navigate(hit.route||'/home');
}

function closeUserMenu(){
  const wrap=el('userMenuWrap');
  const menu=el('userMenu');
  const dropdown=el('userMenuDropdown');
  if(!wrap||!menu||!dropdown)return;
  wrap.classList.remove('open');
  menu.setAttribute('aria-expanded','false');
  dropdown.hidden=true;
}

function openUserMenuDropdown(){
  const wrap=el('userMenuWrap');
  const menu=el('userMenu');
  const dropdown=el('userMenuDropdown');
  if(!wrap||!menu||!dropdown)return;
  wrap.classList.add('open');
  menu.setAttribute('aria-expanded','true');
  dropdown.hidden=false;
}

function bindUserMenu(){
  const wrap=el('userMenuWrap');
  const menu=el('userMenu');
  const dropdown=el('userMenuDropdown');
  if(!wrap||!menu||!dropdown||menu.dataset.bound)return;
  menu.dataset.bound='1';
  menu.onclick=e=>{
    e.stopPropagation();
    if(wrap.classList.contains('open'))closeUserMenu();
    else openUserMenuDropdown();
  };
  dropdown.querySelectorAll('[data-user-action]').forEach(button=>{
    button.onclick=async e=>{
      e.stopPropagation();
      const action=button.dataset.userAction;
      if(action==='theme-menu')return;
      closeUserMenu();
      if(action==='profile'){
        navigate('/profile');
        return;
      }
      if(action==='signout'){
        try{await api('/api/auth/logout',{method:'POST'})}catch{}
        localStorage.removeItem(TOKEN_KEY);state.token=null;renderLogin();
      }
    };
  });
  syncThemeMenuChecks();
  dropdown.querySelectorAll('[data-theme-choice]').forEach(button=>{
    button.onclick=async e=>{
      e.stopPropagation();
      const theme=button.dataset.themeChoice;
      closeUserMenu();
      await setThemePreference(theme);
      syncThemeMenuChecks();
    };
  });
  document.addEventListener('click',e=>{
    if(!wrap.contains(e.target))closeUserMenu();
  });
  document.addEventListener('keydown',e=>{
    if(e.key==='Escape')closeUserMenu();
  });
}

function syncThemeMenuChecks(){
  const current=state.me?.profile?.theme||state.theme||'system';
  document.querySelectorAll('[data-theme-choice]').forEach(btn=>{
    const on=btn.dataset.themeChoice===current;
    btn.classList.toggle('checked',on);
    btn.setAttribute('aria-checked',on?'true':'false');
  });
}

function openUserMenu(){
  openUserMenuDropdown();
  syncThemeMenuChecks();
}

function openProjectSwitcher(){
  if(isOrgRoute(state.route))return;
  const rows=(state.projects||[]).map(p=>`<button class="button" style="width:100%;justify-content:flex-start;margin-bottom:6px" value="project:${esc(p.id)}">${esc(p.name)} <small style="color:var(--muted);margin-left:auto">/${esc(p.slug)}</small></button>`).join('')||'<p class="description">No projects yet.</p>';
  openModal(`<div class="modal-content"><h2>Switch project</h2><p>Choose the active working context for this organisation.</p>
    <div style="margin-top:14px">${rows}</div>
    <div class="modal-actions">
      <button class="button" value="home">Organisation home</button>
      <button class="button" value="cancel">Close</button>
      <button class="button primary" value="new">New project</button>
    </div></div>`,async e=>{
    const value=e.submitter?.value;
    if(value==='home')return navigate('/home');
    if(value==='new'){
      if(!canCreateProject())return showToast('You need permission to create projects.',true);
      return openCreateProject();
    }
    if(value?.startsWith('project:')){
      const id=value.slice(8);
      try{
        await api('/api/core/context/project',{method:'POST',body:JSON.stringify({projectId:id})});
        await loadWorkspace();
        navigate('/overview');
        showToast('Project switched');
      }catch(error){showToast(error.message,true)}
    }
  });
}

function openCreateProject(){
  openProjectWizard();
}

function openProjectWizard(defaults={}){
  const preselectTeam=defaults.owningTeamId||'';
  const loadTeams=can('projects.create')||can('team.projects.create')||can('teams.manage');
  openModal(`<div class="modal-content project-wizard"><h2>Create project</h2>
    <label class="form-label">Name</label><input class="field" id="newProjectName" value="Atlas" autofocus>
    <label class="form-label">Slug</label><input class="field" id="newProjectSlug" value="atlas" placeholder="Generated from name">
    <label class="form-label">Description</label><input class="field" id="newProjectDesc" placeholder="Optional">
    <label class="form-label" for="newProjectTeam">Owning team</label>
    <select class="field" id="newProjectTeam"><option value="">Loading…</option></select>
    <p class="description">The owning team administratively owns this project. Additional teams and members can still be granted access.</p>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      const project=await api('/api/projects',{method:'POST',body:JSON.stringify({
        name:el('newProjectName').value,
        slug:el('newProjectSlug').value||null,
        description:el('newProjectDesc').value||null,
        owningTeamId:el('newProjectTeam').value||null
      })});
      await api('/api/core/context/project',{method:'POST',body:JSON.stringify({projectId:project.id})});
      await loadWorkspace();
      showToast('Project created');
      navigate('/overview');
    }catch(error){showToast(error.message,true)}
  });
  wireAutoSlug('newProjectName','newProjectSlug');
  (async()=>{
    const select=el('newProjectTeam');
    if(!select)return;
    if(!loadTeams && !preselectTeam){
      select.innerHTML='<option value="">No owning team</option>';
      return;
    }
    try{
      const teams=await api('/api/teams');
      const options=['<option value="">No owning team</option>']
        .concat((teams||[]).map(t=>`<option value="${esc(t.id)}" ${t.id===preselectTeam?'selected':''}>${esc(t.name)}</option>`));
      select.innerHTML=options.join('');
      if(preselectTeam && !can('projects.create')){
        select.disabled=true;
      }
    }catch{
      select.innerHTML='<option value="">No owning team</option>';
    }
  })();
}

function renderNavigation(){
  const openCount=openStatuses().length;
  const route=state.route||'/home';
  updateContextChrome();
  let html='';
  if(isOrgRoute(route)){
    html+='<p class="nav-group">Organisation</p>';
    html+='<button type="button" class="nav-item" data-route="/home"><span class="nav-icon">⌂</span>Home</button>';
    html+='<button type="button" class="nav-item" data-route="/projects"><span class="nav-icon">▦</span>Projects</button>';
    html+='<button type="button" class="nav-item" data-route="/people"><span class="nav-icon">◎</span>People</button>';
    html+='<p class="nav-group">Settings</p>';
    html+='<button type="button" class="nav-item" data-route="/organisation/settings"><span class="nav-icon">⚙</span>Organisation settings</button>';
  }else{
    html+='<p class="nav-group">Project</p><button type="button" class="nav-item" data-route="/overview"><span class="nav-icon">⌂</span>Overview</button>';
    const groupOrder={Code:1,Review:2,Build:3,Deploy:4};
    const groups={};
    state.modules.flatMap(module=>module.navigation||[])
      .sort((a,b)=>a.order-b.order)
      .forEach(item=>(groups[item.group]??=[]).push(item));
    Object.entries(groups)
      .sort(([a],[b])=>(groupOrder[a]??50)-(groupOrder[b]??50))
      .forEach(([group,items])=>html+=`<p class="nav-group">${esc(group)}</p>${items.map(item=>{
      const icon=item.id==='files'||item.id==='commits'||item.id==='tags'||item.id==='branches'?'▱'
        :item.id==='changes'?'⑂'
        :item.id==='pipelines'||item.id==='runs'||item.id==='jobs'||item.id==='tests'||item.id==='artifacts'?'≋'
        :item.id==='environments'||item.id==='deployments'?'⇪'
        :item.id==='runners'?'◉':'≋';
      const badge=item.id==='changes'&&openCount?`<span class="nav-badge">${openCount}</span>`:'';
      return `<button type="button" class="nav-item" data-route="${esc(item.route)}"><span class="nav-icon">${icon}</span>${esc(item.label)}${badge}</button>`;
    }).join('')}`);
    html+='<p class="nav-group">Settings</p>';
    html+='<button type="button" class="nav-item" data-route="/settings/members"><span class="nav-icon">◎</span>Members</button>';
    html+='<button type="button" class="nav-item" data-route="/settings"><span class="nav-icon">⚙</span>Project settings</button>';
  }
  el('primaryNav').innerHTML=html;
  setActiveNav(state.route);
}

async function navigate(route,push=true){
  route=normalizeRoute(route);
  state.route=route;
  if(push){
    const next=`#${route}`;
    if(location.hash!==next){
      try{
        hashNavigating=true;
        location.hash=route;
      }catch{
        try{history.replaceState(null,'',next)}catch{/* embedded browsers may block history */}
      }finally{
        hashNavigating=false;
      }
    }
  }
  renderNavigation();
  setActiveNav(route);
  try{
    if(route==='/home')return await renderOrgHome();
    if(route==='/projects')return await renderProjectsPage();
    if(route==='/overview')return await renderOverview();
    if(route==='/changes')return renderChanges();
    if(route==='/queue')return renderQueue();
    if(route.startsWith('/changes/')){const id=route.split('/')[2];state.change=await api(`/api/review/changes/${id}`);state.expandedFiles={};return renderChange()}
    if(route.startsWith('/files')){if(!hasModule('code'))return renderError(new Error('Code module is not enabled.'));return await renderFiles(route)}
    if(route.startsWith('/commits/')){if(!hasModule('code'))return renderError(new Error('Code module is not enabled.'));return await renderCommitDetail(route.split('/')[2])}
    if(route==='/commits'){if(!hasModule('code'))return renderError(new Error('Code module is not enabled.'));return await renderCommits()}
    if(route==='/source-branches'){if(!hasModule('code'))return renderError(new Error('Code module is not enabled.'));return await renderBranches()}
    if(route==='/tags'){if(!hasModule('code'))return renderError(new Error('Code module is not enabled.'));return await renderTags()}
    if(route==='/pipelines'||route.startsWith('/pipelines/')){
      if(route==='/pipelines/new'||route.endsWith('/edit'))return await renderPipelineBuilder(route);
      return await renderPipelines(route);
    }
    if(route==='/environments'||route.startsWith('/environments/'))return await renderEnvironments(route);
    if(route==='/deployments'||route.startsWith('/deployments/'))return await renderDeployments(route);
    if(route==='/runs')return await renderRuns();
    if(route==='/jobs')return await renderJobsList();
    if(route==='/tests')return await renderTestsList();
    if(route==='/artifacts')return await renderArtifactsList();
    {
      const jobMatch=route.match(/^\/runs\/([^/]+)\/jobs\/([^/]+)(?:\/logs)?\/?$/);
      if(jobMatch)return await renderJobPage(jobMatch[1],jobMatch[2]);
    }
    if(route.startsWith('/runs/'))return await renderRunDetail(route.split('/')[2]);
    if(route==='/runners'||route==='/organisation/settings/build')return await renderOrgBuildSettings();
    if(route.startsWith('/organisation/settings'))return await renderOrgSettings(route);
    if(route.startsWith('/settings'))return await renderProjectSettings(route);
    if(route.startsWith('/people/members/'))return await renderMemberDetail(route.split('/')[3]);
    if(route.startsWith('/people/teams/'))return await renderTeamDetail(route.split('/')[3]);
    if(route==='/people')return await renderPeople();
    if(route==='/profile')return await renderProfile();
    if(route.startsWith('/invite/'))return await renderInviteAccept(decodeURIComponent(route.slice('/invite/'.length)));
    return await renderOrgHome();
  }catch(error){renderError(error)}
}

async function renderProfile(){
  const me=state.me||await api('/api/users/me');
  state.me=me;
  const profile=me.profile||{};
  const user=me.user||{};
  const membership=me.membership||{};
  crumbs(orgCrumb('Profile'));
  const theme=profile.theme||state.theme||'system';
  el('content').innerHTML=`
  <section class="profile-page" id="profilePage">
    <header class="list-page-header">
      <div class="profile-hero-identity">
        <span class="avatar lg" id="profileAvatarPreview">${esc(initials(profile.displayName||user.username))}</span>
        <div>
          <h1>Your profile</h1>
          <p class="description">@${esc(user.username||'user')} · ${esc(user.email||'')} · ${esc(membership.role||'Member')}</p>
        </div>
      </div>
    </header>
    <div class="profile-grid">
      <div class="card">
        <div class="card-header"><h2>Account</h2></div>
        <div class="card-body settings-form">
          <label class="form-label" for="profileDisplayName">Display name</label>
          <input class="field" id="profileDisplayName" value="${esc(profile.displayName||'')}">
          <label class="form-label" for="profileJobTitle">Job title</label>
          <input class="field" id="profileJobTitle" value="${esc(profile.jobTitle||'')}" placeholder="Optional">
          <label class="form-label" for="profileBio">Bio</label>
          <textarea class="field" id="profileBio" rows="3" placeholder="A short introduction">${esc(profile.bio||'')}</textarea>
          <label class="form-label" for="profileTimezone">Timezone</label>
          <input class="field" id="profileTimezone" value="${esc(profile.timezone||'')}" placeholder="e.g. Australia/Sydney">
          <label class="form-label" for="profileLocale">Locale</label>
          <input class="field" id="profileLocale" value="${esc(profile.locale||'')}" placeholder="e.g. en-AU">
          <div class="modal-actions" style="margin-top:16px">
            <button class="button primary" id="profileSave">Save profile</button>
          </div>
        </div>
      </div>
      <aside class="profile-side">
        <div class="card">
          <div class="card-header"><h2>Appearance</h2></div>
          <div class="card-body">
            <p class="description">Theme preference is also available from the user menu.</p>
            <div class="theme-choice-row" role="radiogroup" aria-label="Theme">
              ${['light','dark','system'].map(t=>`
                <label class="theme-choice ${theme===t?'active':''}"><input type="radio" name="profileTheme" value="${t}" ${theme===t?'checked':''}> ${t[0].toUpperCase()+t.slice(1)}</label>`).join('')}
            </div>
          </div>
        </div>
        <div class="card">
          <div class="card-header"><h2>Read-only</h2></div>
          <div class="card-body meta-list">
            <div class="meta-row"><span>Username</span><strong>@${esc(user.username||'—')}</strong></div>
            <div class="meta-row"><span>Email</span><strong>${esc(user.email||'—')}</strong></div>
            <div class="meta-row"><span>Role</span><strong>${esc(membership.role||'—')}</strong></div>
          </div>
        </div>
      </aside>
    </div>
  </section>`;
  el('profileSave').onclick=async()=>{
    try{
      const updated=await api('/api/users/me/preferences',{method:'PATCH',body:JSON.stringify({
        displayName:el('profileDisplayName').value,
        jobTitle:el('profileJobTitle').value,
        bio:el('profileBio').value,
        timezone:el('profileTimezone').value,
        locale:el('profileLocale').value
      })});
      if(state.me)state.me.profile=updated;
      paintShell();
      showToast('Profile saved');
      await renderProfile();
    }catch(error){showToast(error.message,true)}
  };
  document.querySelectorAll('input[name="profileTheme"]').forEach(input=>{
    input.onchange=async()=>{
      await setThemePreference(input.value);
      syncThemeMenuChecks();
      document.querySelectorAll('.theme-choice').forEach(label=>label.classList.toggle('active',label.querySelector('input')?.checked));
    };
  });
}

function localStatusCard(){
  if(!state.local?.associated){
    return `<div class="card"><div class="card-header"><h2>Local repository</h2></div><div class="card-body"><p class="description">No local working copy associated.</p><button class="button primary" id="overviewConnectLocal">Connect local repository</button></div></div>`;
  }
  const s=state.local.status||{},ab=s.aheadBehind;
  return `<div class="card"><div class="card-header"><h2>Local repository</h2><span class="pill">${s.isClean?'Clean':'Dirty'}</span></div><div class="card-body">
    <div class="side-stat"><span>Path</span><strong title="${esc(s.root||s.path)}">${esc(s.root||s.path||'—')}</strong></div>
    <div class="side-stat"><span>Branch</span><strong>${esc(s.currentBranch||'—')}</strong></div>
    <div class="side-stat"><span>HEAD</span><strong><code>${esc((s.headSha||'').slice(0,7)||'—')}</code></strong></div>
    <div class="side-stat"><span>Working tree</span><strong>${s.isClean?'Clean':`${s.modifiedFileCount||0} modified`}</strong></div>
    <div class="side-stat"><span>Remote</span><strong>${esc(s.originUrl||s.detectedGitHub?.url||'—')}</strong></div>
    <div class="side-stat"><span>Ahead / behind</span><strong>${ab?`${ab.ahead} ahead · ${ab.behind} behind`:'—'}</strong></div>
    <div style="margin-top:12px"><button class="button" id="overviewOpenFolder">Open folder</button></div>
  </div></div>`;
}

async function openProject(projectId){
  try{
    await api('/api/core/context/project',{method:'POST',body:JSON.stringify({projectId})});
    await loadWorkspace();
    navigate('/overview');
  }catch(error){showToast(error.message,true)}
}

async function toggleProjectStar(projectId,starred){
  try{
    if(starred)await api(`/api/projects/${projectId}/star`,{method:'DELETE'});
    else await api(`/api/projects/${projectId}/star`,{method:'PUT',body:'{}'});
    state.starredProjects=await api('/api/users/me/starred-projects').catch(()=>[]);
    return true;
  }catch(error){showToast(error.message,true);return false}
}

async function renderOrgHome(){
  const org=state.context?.organisation;
  crumbs(orgCrumb('Home'));
  const projects=state.projects||[];
  const starredIds=new Set((state.starredProjects||[]).map(p=>p.id));
  const starred=projects.filter(p=>starredIds.has(p.id));
  let members=[],runs=[];
  const jobs=[];
  jobs.push(api('/api/organisation/members').then(r=>members=r||[]).catch(()=>[]));
  if(hasModule('pipelines'))jobs.push(api('/api/pipelines/runs').then(r=>runs=r||[]).catch(()=>[]));
  await Promise.all(jobs);
  const open=openStatuses();
  const myPrs=open.filter(c=>c.author===actor()||c.reviewers?.some(r=>r.name===actor()));
  const failedRuns=(runs||[]).filter(r=>['Failed','Cancelled'].includes(r.status)).slice(0,5);
  const recentRuns=(runs||[]).slice(0,5);
  const profile=state.me?.profile;
  const greetingName=profile?.displayName||state.me?.user?.username||'there';
  const getStarted=!projects.length?`<div class="card get-started-card" style="margin-bottom:18px"><div class="card-header"><h2>Get started</h2></div>
    <div class="card-body">
      <p class="description">Welcome, ${esc(greetingName)}. Your organisation is ready — create a project to start shipping.</p>
      <ul class="get-started-list">
        <li><button class="button primary" id="homeCreateFirstProject">Create your first Project</button></li>
        <li><button class="button" data-route="/people">Invite people</button></li>
        <li><button class="button" data-route="/organisation/settings/modules">Browse Modules</button></li>
      </ul>
    </div></div>`:'';
  el('content').innerHTML=`
  <div class="list-page-header"><div>
    <h1>${esc(org?.name||'Organisation')}</h1>
    <p class="description">Good ${timeOfDay()}, ${esc(greetingName)} — projects, pull requests, and build health.</p>
  </div>
  <div class="header-actions">
    <button class="button" data-route="/projects">All projects</button>
    <button class="button primary" id="homeNewProject">＋ New project</button>
  </div></div>
  ${getStarted}
  ${!hasModule('code')&&!hasModule('review')&&!hasModule('pipelines')&&!hasModule('deploy')?`<div class="card" style="margin-bottom:18px"><div class="card-header"><h2>Extend ForgeDeck</h2><button class="button primary" data-route="/organisation/settings/modules">Browse Modules</button></div>
    <div class="card-body"><p class="description">Add source browsing, code review, build automation, and more when you are ready.</p></div></div>`:''}
  <div class="home-metrics">
    <article class="metric-card"><p class="metric-label">Projects</p><p class="metric-value">${projects.length}</p></article>
    <article class="metric-card"><p class="metric-label">People</p><p class="metric-value">${members.length||'—'}</p></article>
    ${hasModule('review')?`<article class="metric-card"><p class="metric-label">Open pull requests</p><p class="metric-value">${open.length}</p></article>`:''}
    ${hasModule('pipelines')?`<article class="metric-card"><p class="metric-label">Recent builds</p><p class="metric-value">${recentRuns.length}</p></article>`:''}
  </div>
  <div class="home-grid">
    <section class="card">
      <div class="card-header"><h2>Starred projects</h2><button class="button text" data-route="/projects">View all</button></div>
      <div class="card-body">
        ${(starred.length?starred:projects.slice(0,6)).map(p=>`
          <button type="button" class="project-home-row" data-open-project="${esc(p.id)}">
            <span class="project-avatar">${esc((p.name||'?')[0].toUpperCase())}</span>
            <span><strong>${esc(p.name)}</strong><small>${esc(p.repositoryMode==='MultiRepository'?'Multi-repo':'Single repo')} · ${esc(p.visibility||'Private')}</small></span>
            <span class="pill">${starredIds.has(p.id)?'★ Starred':'Project'}</span>
          </button>`).join('')||'<div class="empty small">No projects yet.</div>'}
      </div>
    </section>
    ${hasModule('review')?`<section class="card">
      <div class="card-header"><h2>My pull requests</h2><button class="button text" data-route="/changes">Review</button></div>
      <div class="card-body">
        ${myPrs.slice(0,6).map(c=>`<button type="button" class="work-row" data-route="/changes/${esc(c.id)}"><span>#${esc(c.externalNumber||c.externalId)} ${esc(c.title)}</span><span class="status ${statusClass(c.status)}">${esc(c.status)}</span></button>`).join('')||'<div class="empty small">No open pull requests assigned to you.</div>'}
      </div>
    </section>`:''}
    ${hasModule('pipelines')?`<section class="card">
      <div class="card-header"><h2>Build health</h2><button class="button text" data-route="/runs">Runs</button></div>
      <div class="card-body">
        ${failedRuns.length?`<p class="description">${failedRuns.length} recent failure${failedRuns.length===1?'':'s'}.</p>`:'<p class="description">No recent build failures.</p>'}
        ${recentRuns.map(r=>`<button type="button" class="work-row" data-route="/runs/${esc(r.id)}"><span>${esc(r.definitionName)} · ${esc(shortId(r.id))}</span><span class="status ${statusClass(r.status)}">${esc(r.status)}</span></button>`).join('')||'<div class="empty small">No builds yet.</div>'}
      </div>
    </section>`:''}
  </div>`;
  el('homeNewProject').onclick=()=>openCreateProject();
  const first=el('homeCreateFirstProject');
  if(first)first.onclick=()=>openCreateProject();
  document.querySelectorAll('[data-open-project]').forEach(btn=>btn.onclick=()=>openProject(btn.dataset.openProject));
}

function timeOfDay(){
  const h=new Date().getHours();
  if(h<12)return 'morning';
  if(h<18)return 'afternoon';
  return 'evening';
}

async function renderProjectsPage(){
  const org=state.context?.organisation;
  crumbs(orgCrumb('Projects'));
  const projects=state.projects||[];
  const starredIds=new Set((state.starredProjects||[]).map(p=>p.id));
  let openCount=hasModule('review')?openStatuses().length:0;
  let latestBuild=null;
  if(hasModule('pipelines')){
    try{const runs=await api('/api/pipelines/runs').catch(()=>[]);latestBuild=runs?.[0]||null}catch{}
  }
  el('content').innerHTML=`
  <div class="list-page-header"><div>
    <h1>Projects</h1>
    <p class="description">All projects in ${esc(org?.name||'this organisation')}.</p>
  </div>
  <div class="header-actions"><button class="button primary" id="projectsNew">＋ New project</button></div></div>
  <div class="projects-grid">
    ${projects.map(p=>{
      const starred=starredIds.has(p.id);
      const isCurrent=p.id===state.context?.project?.id;
      return `<article class="project-card">
        <button type="button" class="project-card-main" data-open-project="${esc(p.id)}">
          <span class="project-avatar large">${esc((p.name||'?')[0].toUpperCase())}</span>
          <div>
            <h2>${esc(p.name)}</h2>
            <p>${esc(p.description||'No description')}</p>
            <div class="tag-row">
              <span class="tag">${esc(p.repositoryMode==='MultiRepository'?'Multi-repository':'Single repository')}</span>
              <span class="tag">${esc(p.visibility||'Private')}</span>
            </div>
          </div>
        </button>
        <div class="project-card-meta">
          ${hasModule('review')&&isCurrent?`<span>Open reviews <strong>${openCount}</strong></span>`:hasModule('review')?'<span>Open reviews <strong>—</strong></span>':''}
          ${hasModule('pipelines')&&isCurrent?`<span>Build <strong class="${latestBuild?statusClass(latestBuild.status):''}">${latestBuild?esc(latestBuild.status):'—'}</strong></span>`:hasModule('pipelines')?'<span>Build <strong>—</strong></span>':''}
          <button type="button" class="button compact" data-star-project="${esc(p.id)}" data-starred="${starred?'1':'0'}">${starred?'★ Unstar':'☆ Star'}</button>
        </div>
      </article>`;
    }).join('')||'<div class="empty">No projects yet. Create one to get started.</div>'}
  </div>`;
  el('projectsNew').onclick=()=>openCreateProject();
  document.querySelectorAll('[data-open-project]').forEach(btn=>btn.onclick=()=>openProject(btn.dataset.openProject));
  document.querySelectorAll('[data-star-project]').forEach(btn=>btn.onclick=async()=>{
    const ok=await toggleProjectStar(btn.dataset.starProject,btn.dataset.starred==='1');
    if(ok)renderProjectsPage();
  });
}

async function renderTags(){
  const project=state.context?.project;
  crumbs(projectCrumb('Code <span>/</span> Tags'));
  if(!source()){
    el('content').innerHTML=`<div class="list-page-header"><div><h1>Tags</h1><p>No repository connected.</p></div></div>${repoPickerHtml()}`;
    bindRepoPicker(()=>renderTags());
    return;
  }
  let tags=[];
  try{
    const gitRepos=await api('/api/git/repositories').catch(()=>[]);
    const native=Array.isArray(gitRepos)?gitRepos[0]:null;
    if(native?.id)tags=await api(`/api/git/repositories/${native.id}/tags`).catch(()=>[]);
  }catch{tags=[]}
  el('content').innerHTML=`
  <div class="list-page-header"><div><h1>Tags</h1><p>Annotated and lightweight tags for this repository.</p></div></div>
  ${repoPickerHtml()}
  <div class="card"><div class="card-body">
    ${(tags||[]).length?(tags||[]).map(t=>`<div class="work-row"><strong>${esc(t.name||t)}</strong><code>${esc((t.commitSha||t.targetSha||t.sha||'').toString().slice(0,12)||'—')}</code></div>`).join(''):'<div class="empty small">No tags available for this source provider.</div>'}
  </div></div>`;
  bindRepoPicker(()=>renderTags());
}

function flattenRunJobs(runs){
  const rows=[];
  (runs||[]).forEach(run=>{(run.jobs||[]).forEach(job=>rows.push({run,job}))});
  return rows;
}

async function renderJobsList(){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  crumbs(projectCrumb('Build <span>/</span> Jobs'));
  const runs=await api('/api/pipelines/runs');
  const rows=flattenRunJobs(runs).slice(0,100);
  el('content').innerHTML=`
  <div class="list-page-header"><div><h1>Jobs</h1><p>Jobs across recent build runs.</p></div>
  <div class="header-actions"><button class="button" id="refreshJobs">Refresh</button></div></div>
  <div class="card">${rows.map(({run,job})=>`<button type="button" class="job-list-row ${statusClass(job.status)}" data-route="/runs/${esc(run.id)}/jobs/${esc(job.id)}">
    <span class="run-status ${statusClass(job.status)}">${checkIcon(job.status)}</span>
    <div class="job-list-copy"><h3>${esc(job.name)}</h3><p>${esc(run.definitionName)} · ${esc(shortId(run.id))} · ${esc(job.status)}</p></div>
    <span class="status ${statusClass(job.status)}">${esc(job.status)}</span>
  </button>`).join('')||'<div class="empty">No jobs yet.</div>'}</div>`;
  el('refreshJobs').onclick=renderJobsList;
}

async function renderTestsList(){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  crumbs(projectCrumb('Build <span>/</span> Tests'));
  const runs=await api('/api/pipelines/runs');
  const rows=[];
  (runs||[]).forEach(run=>{(run.jobs||[]).forEach(job=>{if(job.testResults)rows.push({run,job,tr:job.testResults})})});
  el('content').innerHTML=`
  <div class="list-page-header"><div><h1>Tests</h1><p>Test results published by build jobs.</p></div>
  <div class="header-actions"><button class="button" id="refreshTests">Refresh</button></div></div>
  <div class="card"><div class="card-body">
    ${rows.length?rows.map(({run,job,tr})=>`<button type="button" class="work-row" data-route="/runs/${esc(run.id)}/jobs/${esc(job.id)}">
      <span><strong>${esc(job.name)}</strong> · ${esc(run.definitionName)}<small>${esc(tr.total??0)} total · ${esc(tr.passed??tr.succeeded??0)} passed · ${esc(tr.failed??0)} failed</small></span>
      <span class="status ${statusClass((tr.failed||0)>0?'Failed':'Succeeded')}">${(tr.failed||0)>0?'Failed':'Passed'}</span>
    </button>`).join(''):'<div class="empty">No test results published yet.</div>'}
  </div></div>`;
  el('refreshTests').onclick=renderTestsList;
}

async function renderArtifactsList(){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  crumbs(projectCrumb('Build <span>/</span> Artifacts'));
  const runs=await api('/api/pipelines/runs');
  const rows=[];
  (runs||[]).forEach(run=>{(run.jobs||[]).forEach(job=>{(job.artifacts||[]).forEach(artifact=>rows.push({run,job,artifact}))})});
  el('content').innerHTML=`
  <div class="list-page-header"><div><h1>Artifacts</h1><p>Artifacts published by build jobs.</p></div>
  <div class="header-actions"><button class="button" id="refreshArtifacts">Refresh</button></div></div>
  <div class="card"><div class="card-body">
    ${rows.length?rows.map(({run,job,artifact})=>`<div class="work-row">
      <span><strong>${esc(artifact.name||artifact.fileName||'artifact')}</strong><small>${esc(job.name)} · ${esc(run.definitionName)} · ${esc(shortId(run.id))}</small></span>
      <button class="button compact" data-route="/runs/${esc(run.id)}/jobs/${esc(job.id)}">Open job</button>
    </div>`).join(''):'<div class="empty">No artifacts published yet.</div>'}
  </div></div>`;
  el('refreshArtifacts').onclick=renderArtifactsList;
}

function filterOverviewPullRequests(changes,filter,actorName){
  const list=changes||[];
  switch(filter){
    case 'assigned':
      return list.filter(c=>(c.reviewers||[]).some(r=>r.name===actorName));
    case 'draft':
      return list.filter(c=>c.status==='Draft');
    case 'abandoned':
      return list.filter(c=>c.status==='Closed');
    case 'all':
      return list.slice();
    case 'open':
    default:
      return list.filter(c=>!['Merged','Closed'].includes(c.status));
  }
}

async function renderOverview(){
  const project=state.context?.project;const org=state.context?.organisation;
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')}`);
  const filter=state.overviewPrFilter||'open';
  const me=actor();
  const filtered=filterOverviewPullRequests(state.changes,filter,me);
  const sortedPrs=filtered.slice().sort((a,b)=>new Date(b.updatedAt||b.createdAt)-new Date(a.updatedAt||a.createdAt)).slice(0,12);

  let runs=[],commits=[];
  try{
    const jobs=[];
    if(hasModule('pipelines'))jobs.push(api('/api/pipelines/runs').then(r=>runs=r||[]).catch(()=>[]));
    if(hasModule('code')&&source()){
      jobs.push(api(`/api/source/repositories/${source().id}/commits?branch=${encodeURIComponent(source().defaultBranch||'main')}`)
        .then(r=>commits=r||[]).catch(()=>[]));
    }
    await Promise.all(jobs);
  }catch{/* overview widgets degrade gracefully */}

  const local=state.local?.status;
  const defaultBranch=source()?.defaultBranch||local?.currentBranch||'main';
  const repoLabel=source()?`${source().repositoryId.owner}/${source().repositoryId.name}`:(project?.name||'Repository');
  const desc=project?.description||`${org?.name||'Organisation'} delivery workspace.`;
  const recentRuns=runs.slice(0,5);
  const recentCommits=commits.slice(0,5);
  const filters=[
    {id:'assigned',label:'Assigned to me'},
    {id:'draft',label:'Draft'},
    {id:'open',label:'Open'},
    {id:'abandoned',label:'Abandoned'},
    {id:'all',label:'All'}
  ];

  el('content').innerHTML=`
  <section class="repo-home" id="repoHome">
    <header class="repo-home-hero">
      <div class="repo-home-identity">
        <div class="project-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><path d="M4 17L12 5l8 12H4z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="M8 17h8" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </div>
        <div>
          <h1>${esc(repoLabel)} <span class="pill visibility-pill">${esc(project?.visibility||'Organisation')}</span></h1>
          <p class="project-desc">${esc(desc)}</p>
          <p class="repo-meta"><span>Default branch</span> <strong>${esc(defaultBranch)}</strong>
            ${source()?` · <span>${esc(source().repositoryId.provider||'git')}</span>`:''}
          </p>
        </div>
      </div>
      <div class="header-actions">
        ${hasModule('code')?'<button class="button" data-route="/files">Browse files</button>':''}
        ${hasModule('review')?'<button class="button" data-route="/changes">Open PRs</button>':''}
        ${hasModule('pipelines')?'<button class="button primary" data-route="/pipelines">Run pipeline</button>':''}
      </div>
    </header>

    <div class="repo-home-grid">
      <section class="card repo-pr-panel">
        <div class="card-header">
          <h2>Pull requests</h2>
          ${hasModule('review')?'<button class="button text" data-route="/changes">View all</button>':''}
        </div>
        <div class="card-body">
          <div class="repo-pr-filters" role="tablist" aria-label="Pull request filters">
            ${filters.map(f=>`<button type="button" class="button ${filter===f.id?'primary':''}" data-pr-filter="${f.id}" role="tab" aria-selected="${filter===f.id?'true':'false'}">${esc(f.label)}</button>`).join('')}
          </div>
          ${!hasModule('review')
            ?'<div class="empty small">Review module is not enabled for this project.</div>'
            :(sortedPrs.length?`<div class="repo-pr-list">${sortedPrs.map(c=>`
              <button type="button" class="repo-pr-row" data-route="/changes/${esc(c.id)}">
                <span class="pill ${statusClass(c.status)}">${esc(c.status)}</span>
                <span class="repo-pr-title"><strong>#${esc(c.externalNumber||'')} ${esc(c.title||'Untitled')}</strong>
                  <small>${esc(c.author||'')} · ${esc(relativeTime(c.updatedAt||c.createdAt))}</small>
                </span>
              </button>`).join('')}</div>`
              :`<div class="empty small">No pull requests for “${esc(filters.find(f=>f.id===filter)?.label||filter)}”.</div>`)}
        </div>
      </section>

      <aside class="repo-home-side">
        <div class="card">
          <div class="card-header"><h2>About</h2><button class="button text" data-route="/settings/general">Edit</button></div>
          <div class="card-body">
            <p class="description">${esc(desc)}</p>
            <div class="meta-list" style="margin-top:12px">
              <div class="meta-row"><span>Project</span><strong>${esc(project?.name||'—')}</strong></div>
              <div class="meta-row"><span>Organisation</span><strong>${esc(org?.name||'—')}</strong></div>
              <div class="meta-row"><span>Default branch</span><strong>${esc(defaultBranch)}</strong></div>
            </div>
          </div>
        </div>
        ${hasModule('code')?`<div class="card"><div class="card-header"><h2>Recent commits</h2><button class="button text" data-route="/commits">History</button></div>
          <div class="card-body">${recentCommits.length?recentCommits.map(c=>`
            <button type="button" class="commit-row" data-route="/commits/${esc(c.sha)}" style="width:100%;border:0;background:transparent;cursor:pointer;text-align:left">
              <code>${esc((c.sha||'').slice(0,7))}</code>
              <div><strong>${esc(c.message||c.subject||'Commit')}</strong><small>${esc(c.author?.name||c.author||'')}</small></div>
            </button>`).join(''):'<div class="empty small">No recent commits.</div>'}
          </div></div>`:''}
        ${hasModule('pipelines')?`<div class="card"><div class="card-header"><h2>Latest runs</h2><button class="button text" data-route="/runs">View all</button></div>
          <div class="card-body">${recentRuns.length?recentRuns.map(run=>`<div class="run-mini" data-route="/runs/${esc(run.id)}">
            <span class="run-status ${statusClass(run.status)}">${checkIcon(run.status)}</span>
            <div style="flex:1;min-width:0"><strong>${esc(run.definitionName||'Pipeline')}</strong><div><code>#${esc(shortId(run.id))}</code></div></div>
            <span class="status ${statusClass(run.status)}">${esc(run.status)}</span>
          </div>`).join(''):'<div class="empty small">No runs yet.</div>'}
          </div></div>`:''}
      </aside>
    </div>
  </section>`;

  document.querySelectorAll('[data-pr-filter]').forEach(btn=>{
    btn.onclick=()=>{
      state.overviewPrFilter=btn.dataset.prFilter;
      renderOverview();
    };
  });
}

function buildOverviewActivity(open,merged,runs,audit){
  const items=[];
  open.slice(0,3).forEach(c=>items.push({
    actor:c.author||'Someone',initials:initials(c.author),
    detail:`opened change #${c.externalNumber||c.externalId||''} — ${c.title||'untitled'}`,
    when:relativeTime(c.updatedAt||c.createdAt),ts:new Date(c.updatedAt||c.createdAt||0).getTime()
  }));
  merged.slice(0,2).forEach(c=>items.push({
    actor:c.author||'Someone',initials:initials(c.author),
    detail:`merged pull request #${c.externalNumber||c.externalId||''}`,
    when:relativeTime(c.mergedAt||c.updatedAt),ts:new Date(c.mergedAt||c.updatedAt||0).getTime()
  }));
  runs.slice(0,3).forEach(r=>items.push({
    actor:'Pipelines',initials:'PL',
    detail:`${String(r.status).toLowerCase()} run ${r.definitionName||''}`.trim(),
    when:relativeTime(r.completedAt||r.startedAt),ts:new Date(r.completedAt||r.startedAt||0).getTime()
  }));
  (audit||[]).slice(0,4).forEach(a=>items.push({
    actor:a.actor||'System',initials:initials(a.actor||'S'),
    detail:`${a.action||'action'} on ${a.resource||a.module||'platform'}`,
    when:relativeTime(a.timestamp),ts:new Date(a.timestamp||0).getTime()
  }));
  return items.sort((a,b)=>b.ts-a.ts).slice(0,6);
}

function relativeTime(value){
  if(!value)return '—';
  const then=new Date(value).getTime();
  if(Number.isNaN(then))return '—';
  const seconds=Math.max(0,Math.round((Date.now()-then)/1000));
  if(seconds<60)return 'just now';
  if(seconds<3600)return `${Math.floor(seconds/60)}m ago`;
  if(seconds<86400)return `${Math.floor(seconds/3600)}h ago`;
  return `${Math.floor(seconds/86400)}d ago`;
}

async function renderPeople(){
  const tab=state.peopleTab||'members';
  crumbs(orgCrumb('People'));
  const tabs=`<div class="filterbar">
    <button class="button ${tab==='members'?'primary':''}" id="peopleMembers">Members</button>
    <button class="button ${tab==='teams'?'primary':''}" id="peopleTeams">Teams</button>
    <button class="button ${tab==='roles'?'primary':''}" id="peopleRoles">Roles</button>
    <button class="button ${tab==='invitations'?'primary':''}" id="peopleInvites">Invitations</button>
  </div>`;
  if(tab==='teams')return renderPeopleTeams(tabs);
  if(tab==='roles')return renderPeopleRoles(tabs);
  if(tab==='invitations')return renderPeopleInvitations(tabs);
  const [members,teams]=await Promise.all([
    api('/api/organisation/members'),
    api('/api/teams').catch(()=>[])
  ]);
  const teamCountByUser={};
  await Promise.all((teams||[]).map(async team=>{
    try{
      const detail=await api(`/api/teams/${team.id}`);
      (detail.members||[]).forEach(m=>{
        const id=m.user?.id||m.userId;
        if(id)teamCountByUser[id]=(teamCountByUser[id]||0)+1;
      });
    }catch{/* ignore */}
  }));
  const canInvite=can('users.manage')||can('users.invite');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>People</h1><p>Organisation members, teams, and roles.</p></div>
    ${canInvite?'<button class="button primary" id="inviteMember">Invite member</button>':''}</div>${tabs}
    <div class="card table-wrap"><table class="data-table">
      <thead><tr><th>Account</th><th>Organisation role</th><th>Teams</th><th>Expiration</th><th>Last activity</th></tr></thead>
      <tbody>${members.map(m=>{
        const userId=m.user?.id;
        const name=m.profile?.displayName||m.user?.username||'Member';
        return `<tr class="click-row" data-member="${esc(userId)}">
          <td><div class="table-identity"><span class="avatar sm">${esc(initials(name))}</span><div><strong>${esc(name)}</strong><div class="muted">@${esc(m.user?.username)} · ${esc(m.user?.email)}</div></div></div></td>
          <td>${esc(m.membership?.role||'Member')}</td>
          <td>${teamCountByUser[userId]||0}</td>
          <td>${m.membership?.expiresAt?esc(new Date(m.membership.expiresAt).toLocaleDateString()):'Never'}</td>
          <td>${esc(relativeTime(m.user?.lastLoginAt||m.membership?.joinedAt))}</td>
        </tr>`;
      }).join('')||'<tr><td colspan="5"><div class="empty">No members.</div></td></tr>'}</tbody>
    </table></div>`;
  wirePeopleTabs();
  document.querySelectorAll('[data-member]').forEach(row=>row.onclick=()=>navigate(`/people/members/${row.dataset.member}`));
  const invite=el('inviteMember');
  if(invite)invite.onclick=()=>openInviteMemberModal();
}

function openInviteMemberModal(){
  openModal(`<div class="modal-content"><h2>Invite member</h2>
    <label class="form-label">Email</label><input class="field" id="inviteEmail">
    <label class="form-label">Role</label><select class="field" id="inviteRole"><option>Member</option><option>Admin</option></select>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create invite</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      const created=await api('/api/organisation/invitations',{method:'POST',body:JSON.stringify({
        email:el('inviteEmail').value,role:el('inviteRole').value
      })});
      const link=`${location.origin}${location.pathname}${created.acceptPath}`;
      openModal(`<div class="modal-content"><h2>Invitation created</h2>
        <p class="description">Copy this link and share it. The token is shown once.</p>
        <input class="field" id="inviteLink" value="${esc(link)}" readonly>
        <div class="modal-actions"><button class="button primary" value="copy">Copy link</button></div></div>`,ev=>{
        if(ev.submitter?.value==='copy'){navigator.clipboard?.writeText(el('inviteLink').value);showToast('Link copied')}
      });
    }catch(error){showToast(error.message,true)}
  });
}

async function renderPeopleTeams(tabs){
  const teams=await api('/api/teams');
  const canCreate=can('teams.manage')||can('teams.create');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Teams</h1><p>Group people and own or access projects.</p></div>
    ${canCreate?'<button class="button primary" id="createTeam">New team</button>':''}</div>${tabs}
    <div class="card table-wrap"><table class="data-table">
      <thead><tr><th>Team</th><th>Description</th><th>Updated</th><th></th></tr></thead>
      <tbody>${(teams||[]).map(t=>`<tr class="click-row" data-team="${esc(t.id)}">
        <td><strong>${esc(t.name)}</strong><div class="muted">/${esc(t.slug)}</div></td>
        <td>${esc(t.description||'—')}</td>
        <td>${esc(relativeTime(t.updatedAt||t.createdAt))}</td>
        <td><button class="button text" data-open-team="${esc(t.id)}">Open</button></td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty">No teams yet.</div></td></tr>'}</tbody>
    </table></div>`;
  wirePeopleTabs();
  const create=el('createTeam');
  if(create)create.onclick=()=>openModal(`<div class="modal-content"><h2>Create team</h2>
    <label class="form-label" for="teamName">Name</label><input class="field" id="teamName" value="Platform">
    <label class="form-label" for="teamSlug">Slug</label><input class="field" id="teamSlug" value="platform">
    <label class="form-label" for="teamDesc">Description</label><input class="field" id="teamDesc" placeholder="Optional">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create team</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      const team=await api('/api/teams',{method:'POST',body:JSON.stringify({
        name:el('teamName').value,
        slug:el('teamSlug').value,
        description:el('teamDesc').value||null
      })});
      showToast('Team created');
      navigate(`/people/teams/${team.id}`);
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-team],[data-open-team]').forEach(elBtn=>elBtn.onclick=()=>navigate(`/people/teams/${elBtn.dataset.team||elBtn.dataset.openTeam}`));
}

async function renderPeopleRoles(tabs){
  const roles=await api('/api/access/roles').catch(()=>[]);
  const orgRoles=(roles||[]).filter(r=>!r.scopeType||r.scopeType==='Organisation');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Roles</h1><p>Organisation roles and reusable templates. Team-specific roles are managed on each team.</p></div>
    ${canAny('roles.manage','users.manage')?'<button class="button primary" id="createOrgRole">Create role</button>':''}
    <button class="button" data-route="/organisation/settings/permissions">Permission catalogue</button></div>${tabs}
    <div class="card">${orgRoles.map(role=>`<div class="module-card">
      <div><h3>${esc(role.name)}</h3><p>${role.isSystem?'System role':'Custom'} · ${role.permissions?.length||0} permissions${role.description?` · ${esc(role.description)}`:''}</p></div>
      <span class="module-state">${esc(role.scopeType||'Organisation')}</span>
    </div>`).join('')||'<div class="empty">No organisation roles.</div>'}</div>`;
  wirePeopleTabs();
  const create=el('createOrgRole');
  if(create)create.onclick=async()=>{
    const catalogue=await api('/api/access/permissions').catch(()=>[]);
    openAccessRoleEditor(null,catalogue);
  };
}

async function renderPeopleInvitations(tabs){
  const invitations=await api('/api/organisation/invitations');
  const canInvite=can('users.manage')||can('users.invite');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Invitations</h1><p>Pending and accepted invitations.</p></div>
    ${canInvite?'<button class="button primary" id="inviteMember">Invite</button>':''}</div>${tabs}
    <div class="card">${invitations.map(i=>`<div class="module-card">
      <div><h3>${esc(i.email)}</h3><p>${esc(i.role)} · expires ${esc(new Date(i.expiresAt).toLocaleString())}${i.acceptedAt?' · accepted':''}</p></div>
      <span class="module-state">${i.acceptedAt?'Accepted':'Pending'}</span>
    </div>`).join('')||'<div class="empty">No invitations.</div>'}</div>`;
  wirePeopleTabs();
  const invite=el('inviteMember');
  if(invite)invite.onclick=()=>openInviteMemberModal();
}

function wirePeopleTabs(){
  el('peopleMembers').onclick=()=>{state.peopleTab='members';renderPeople()};
  el('peopleTeams').onclick=()=>{state.peopleTab='teams';renderPeople()};
  const roles=el('peopleRoles');if(roles)roles.onclick=()=>{state.peopleTab='roles';renderPeople()};
  el('peopleInvites').onclick=()=>{state.peopleTab='invitations';renderPeople()};
}

async function renderMemberDetail(userId){
  const detail=await api(`/api/organisation/members/${userId}`);
  const tab=state.memberDetailTab||'overview';
  const name=detail.profile?.displayName||detail.user?.username||'Member';
  crumbs(orgCrumb(`People <span>/</span> Members <span>/</span> ${esc(name)}`));
  const tabs=`<div class="filterbar">
    ${['overview','teams','projects','permissions','activity'].map(id=>`<button class="button ${tab===id?'primary':''}" data-member-tab="${id}">${id[0].toUpperCase()+id.slice(1)}</button>`).join('')}
  </div>`;
  const canManage=can('users.manage');
  let body='';
  if(tab==='overview'){
    body=`<div class="panel-grid"><div class="card"><div class="card-body">
      <div class="side-stat"><span>Status</span><strong>${esc(detail.membership?.status||'—')}</strong></div>
      <div class="side-stat"><span>Organisation role</span><strong>${esc(detail.membership?.role||'—')}</strong></div>
      <div class="side-stat"><span>Member since</span><strong>${esc(detail.membership?.joinedAt?new Date(detail.membership.joinedAt).toLocaleDateString():'—')}</strong></div>
      <div class="side-stat"><span>Expiration</span><strong>${detail.membership?.expiresAt?esc(new Date(detail.membership.expiresAt).toLocaleDateString()):'Never'}</strong></div>
      <div class="side-stat"><span>Last activity</span><strong>${esc(relativeTime(detail.user?.lastLoginAt))}</strong></div>
    </div></div>
    <div class="metric-grid">
      <article class="metric-card"><p class="metric-label">Teams</p><p class="metric-value">${detail.summary?.teams||0}</p></article>
      <article class="metric-card"><p class="metric-label">Projects</p><p class="metric-value">${detail.summary?.projects||0}</p></article>
      <article class="metric-card"><p class="metric-label">Direct permissions</p><p class="metric-value">${detail.summary?.directPermissions||0}</p></article>
    </div></div>
    ${canManage?`<div class="modal-actions" style="margin-top:16px">
      <button class="button" id="memberSuspend">${detail.membership?.status==='Suspended'?'Reactivate':'Suspend'}</button>
      <button class="button danger" id="memberRemove">Remove</button>
    </div>`:''}`;
  }else if(tab==='teams'){
    body=`<div class="card table-wrap"><table class="data-table"><thead><tr><th>Team</th><th>Role</th><th>Expiration</th><th></th></tr></thead>
      <tbody>${(detail.teams||[]).map(t=>`<tr>
        <td><button class="button text" data-route="/people/teams/${esc(t.teamId)}">${esc(t.teamName||t.teamId)}</button></td>
        <td>${esc(t.roleName||'Team Member')}</td>
        <td>${t.expiresAt?esc(new Date(t.expiresAt).toLocaleDateString()):'Never'}</td>
        <td>${t.active?'Active':'Expired'}</td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty">Not on any teams.</div></td></tr>'}</tbody></table></div>`;
  }else if(tab==='projects'){
    body=`<div class="card table-wrap"><table class="data-table"><thead><tr><th>Project</th><th>Access</th><th>Role</th><th>Source</th></tr></thead>
      <tbody>${(detail.projects||[]).map(p=>`<tr>
        <td>${esc(p.name)}</td>
        <td>${esc(p.accessType)}</td>
        <td>${esc((p.roles||[]).join(' + ')||'—')}</td>
        <td>${esc((p.sources||[]).map(s=>s.teamName).filter(Boolean).join(', ')||(p.accessType.includes('Direct')?'Direct assignment':'—'))}</td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty">No project access.</div></td></tr>'}</tbody></table></div>`;
  }else if(tab==='permissions'){
    body=`<div class="card"><div class="card-header"><h2>Effective organisation permissions</h2>
      ${canAny('permissions.manage','users.manage')?'<button class="button" id="manageDirectPerms">Manage direct permissions</button>':''}
    </div>
    <div class="card-body table-wrap"><table class="data-table"><thead><tr><th>Permission</th><th>Effective</th><th></th></tr></thead>
      <tbody>${(detail.permissions||[]).map(p=>`<tr>
        <td><code>${esc(p)}</code></td><td>Yes</td>
        <td><button class="button text" data-explain="${esc(p)}">Why?</button></td>
      </tr>`).join('')||'<tr><td colspan="3"><div class="empty">No effective permissions.</div></td></tr>'}</tbody></table></div></div>`;
  }else{
    body=`<div class="card"><div class="card-body"><p class="description">Membership and access changes appear in organisation audit.</p>
      <button class="button" data-route="/organisation/settings/audit">Open audit log</button></div></div>`;
  }
  el('content').innerHTML=`<div class="list-page-header"><div>
    <p class="eyebrow"><button class="button text" data-route="/people">← People</button></p>
    <h1>${esc(name)}</h1>
    <p class="description">@${esc(detail.user?.username)} · ${esc(detail.user?.email)} · ${esc(detail.membership?.role)}</p>
  </div></div>${tabs}${body}`;
  document.querySelectorAll('[data-member-tab]').forEach(btn=>btn.onclick=()=>{state.memberDetailTab=btn.dataset.memberTab;renderMemberDetail(userId)});
  document.querySelectorAll('[data-explain]').forEach(btn=>btn.onclick=()=>openPermissionExplain(btn.dataset.explain,'Organisation',null,userId));
  const suspend=el('memberSuspend');
  if(suspend)suspend.onclick=async()=>{
    try{
      const next=detail.membership?.status==='Suspended'?'Active':'Suspended';
      await api(`/api/organisation/members/${userId}/status`,{method:'PATCH',body:JSON.stringify({status:next})});
      showToast(`Member ${next==='Active'?'reactivated':'suspended'}`);
      renderMemberDetail(userId);
    }catch(error){showToast(error.message,true)}
  };
  const remove=el('memberRemove');
  if(remove)remove.onclick=async()=>{
    if(!confirm(`Remove ${name} from the organisation?`))return;
    try{
      await api(`/api/organisation/members/${userId}`,{method:'DELETE'});
      showToast('Member removed');navigate('/people');
    }catch(error){showToast(error.message,true)}
  };
  const manage=el('manageDirectPerms');
  if(manage)manage.onclick=()=>openGrantDirectPermission(userId);
}

async function openGrantDirectPermission(userId){
  const catalogue=await api('/api/access/permissions').catch(()=>[]);
  openModal(`<div class="modal-content"><h2>Grant direct permission</h2>
    <p class="description">Advanced escape hatch. Prefer roles for normal administration.</p>
    <label class="form-label">Permission</label>
    <select class="field" id="grantPermission">${(catalogue||[]).map(p=>`<option value="${esc(p.key)}">${esc(p.key)} — ${esc(p.title)}</option>`).join('')}</select>
    <label class="form-label">Scope</label>
    <select class="field" id="grantScope"><option value="Organisation">Organisation</option></select>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Grant</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      await api('/api/access/grants',{method:'POST',body:JSON.stringify({
        userId,
        permissionId:el('grantPermission').value,
        scopeType:el('grantScope').value
      })});
      showToast('Permission granted');
      state.memberDetailTab='permissions';
      renderMemberDetail(userId);
    }catch(error){showToast(error.message,true)}
  });
}

async function renderTeamDetail(teamId){
  const [detail,roles,projects]=await Promise.all([
    api(`/api/teams/${teamId}`),
    api('/api/access/roles?scopeType=Team').catch(()=>api('/api/access/roles').catch(()=>[])),
    api('/api/projects').catch(()=>[])
  ]);
  const team=detail.team;
  const tab=state.teamDetailTab||'overview';
  crumbs(orgCrumb(`People <span>/</span> Teams <span>/</span> ${esc(team.name)}`));
  const teamRoles=(roles||[]).filter(r=>r.scopeType==='Team'||['team-lead','team-member'].includes(r.slug));
  const tabs=`<div class="filterbar">
    ${['overview','members','projects','roles','permissions','settings'].map(id=>`<button class="button ${tab===id?'primary':''}" data-team-tab="${id}">${id[0].toUpperCase()+id.slice(1)}</button>`).join('')}
  </div>`;
  const canManageMembers=can('teams.manage')||can('team.members.manage');
  const canCreateProject=can('projects.create')||can('team.projects.create');
  const leads=(detail.members||[]).filter(m=>m.role?.slug==='team-lead');
  let body='';
  if(tab==='overview'){
    body=`<div class="metric-grid">
      <article class="metric-card"><p class="metric-label">Leads</p><p class="metric-value">${leads.length}</p><p class="metric-trend flat">${leads.map(m=>m.profile?.displayName||m.user?.username).join(', ')||'None'}</p></article>
      <article class="metric-card"><p class="metric-label">Members</p><p class="metric-value">${(detail.members||[]).length}</p></article>
      <article class="metric-card"><p class="metric-label">Projects</p><p class="metric-value">${(projects||[]).filter(p=>p.owningTeamId===teamId).length}</p></article>
    </div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>About</h2></div>
      <div class="card-body"><p class="description">${esc(team.description||'No description.')}</p></div></div>`;
  }else if(tab==='members'){
    body=`<div class="card"><div class="card-header"><h2>Members</h2>
      ${canManageMembers?'<button class="button primary" id="teamAddMember">Add member</button>':''}
    </div>
    <div class="card-body table-wrap"><table class="data-table"><thead><tr><th>Member</th><th>Team role</th><th>Expiration</th><th></th></tr></thead>
      <tbody>${(detail.members||[]).map(m=>`<tr>
        <td><button class="button text" data-route="/people/members/${esc(m.user?.id)}">${esc(m.profile?.displayName||m.user?.username)}</button></td>
        <td>${esc(m.role?.name||'Team Member')}</td>
        <td>${m.membership?.expiresAt?esc(new Date(m.membership.expiresAt).toLocaleDateString()):'Never'}</td>
        <td>${canManageMembers?`<button class="button danger text" data-remove-member="${esc(m.user?.id)}">Remove</button>`:''}</td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty">No members.</div></td></tr>'}</tbody></table></div></div>`;
  }else if(tab==='projects'){
    const owned=(projects||[]).filter(p=>p.owningTeamId===teamId);
    body=`<div class="card"><div class="card-header"><h2>Projects</h2>
      ${canCreateProject?`<button class="button primary" id="teamNewProject">New project</button>`:''}
    </div>
    <div class="card-body">${owned.map(p=>`<div class="module-card"><div><h3>${esc(p.name)}</h3><p>/${esc(p.slug)} · Owner</p></div></div>`).join('')||'<div class="empty">No owned projects yet.</div>'}</div></div>`;
  }else if(tab==='roles'){
    body=`<div class="card"><div class="card-header"><h2>Team roles</h2></div>
      <div class="card-body">${teamRoles.map(r=>`<div class="module-card"><div><h3>${esc(r.name)}</h3><p>${r.isSystem?'System':'Custom'} · ${r.permissions?.length||0} permissions${r.defaultProjectRoleId?' · has default project role':''}</p></div></div>`).join('')||'<div class="empty">No team roles.</div>'}</div></div>`;
  }else if(tab==='permissions'){
    body=`<div class="card"><div class="card-body"><p class="description">Team administration is granted through Team Lead / custom team roles and direct grants.</p>
      <button class="button" data-route="/organisation/settings/permissions">Open permission settings</button></div></div>`;
  }else{
    body=`<div class="card"><div class="card-body settings-form">
      <label class="form-label">Name</label><input class="field" id="teamSettingsName" value="${esc(team.name)}" ${can('teams.manage')?'':'disabled'}>
      <label class="form-label">Description</label><input class="field" id="teamSettingsDesc" value="${esc(team.description||'')}" ${can('teams.manage')?'':'disabled'}>
      ${can('teams.manage')?'<div class="modal-actions" style="margin-top:16px"><button class="button primary" id="teamSettingsSave">Save</button></div>':''}
    </div></div>`;
  }
  el('content').innerHTML=`<div class="list-page-header"><div>
    <p class="eyebrow"><button class="button text" data-route="/people">← People</button></p>
    <h1>${esc(team.name)}</h1>
    <p class="description">/${esc(team.slug)}</p>
  </div></div>${tabs}${body}`;
  document.querySelectorAll('[data-team-tab]').forEach(btn=>btn.onclick=()=>{state.teamDetailTab=btn.dataset.teamTab;renderTeamDetail(teamId)});
  const add=el('teamAddMember');
  if(add)add.onclick=async()=>{
    const members=await api('/api/organisation/members');
    openModal(`<div class="modal-content"><h2>Add member</h2>
      <label class="form-label">Member</label><select class="field" id="teamMemberId">${members.map(m=>`<option value="${esc(m.user.id)}">${esc(m.profile?.displayName||m.user.username)}</option>`).join('')}</select>
      <label class="form-label">Team role</label><select class="field" id="teamMemberRole">${teamRoles.map(r=>`<option value="${esc(r.id)}" ${r.slug==='team-member'?'selected':''}>${esc(r.name)}</option>`).join('')}</select>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Add</button></div></div>`,async e=>{
      if(e.submitter?.value!=='submit')return;
      try{
        await api(`/api/teams/${teamId}/members`,{method:'POST',body:JSON.stringify({
          userId:el('teamMemberId').value,
          roleId:el('teamMemberRole').value||null
        })});
        showToast('Member added');renderTeamDetail(teamId);
      }catch(error){showToast(error.message,true)}
    });
  };
  document.querySelectorAll('[data-remove-member]').forEach(btn=>btn.onclick=async()=>{
    try{
      await api(`/api/teams/${teamId}/members/${btn.dataset.removeMember}`,{method:'DELETE'});
      showToast('Member removed');renderTeamDetail(teamId);
    }catch(error){showToast(error.message,true)}
  });
  const newProject=el('teamNewProject');
  if(newProject)newProject.onclick=()=>openProjectWizard({owningTeamId:teamId});
  const save=el('teamSettingsSave');
  if(save)save.onclick=async()=>{
    try{
      await api(`/api/teams/${teamId}`,{method:'PATCH',body:JSON.stringify({
        name:el('teamSettingsName').value,
        description:el('teamSettingsDesc').value
      })});
      showToast('Team updated');renderTeamDetail(teamId);
    }catch(error){showToast(error.message,true)}
  };
}

async function renderInviteAccept(token){
  el('appSidebar').style.display='none';
  document.querySelector('.app-shell')?.classList.add('setup-mode');
  crumbs('Invitation');
  let preview;
  try{preview=await api(`/api/invitations/${encodeURIComponent(token)}`)}catch(error){
    el('content').innerHTML=`<div class="setup-shell"><div class="setup-card"><h1>Invitation unavailable</h1><p class="description">${esc(error.message)}</p></div></div>`;
    return;
  }
  if(preview.accepted||preview.expired){
    el('content').innerHTML=`<div class="setup-shell"><div class="setup-card"><h1>Invitation unavailable</h1><p class="description">${preview.accepted?'Already accepted.':'This invitation has expired.'}</p></div></div>`;
    return;
  }
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <h1>Join organisation</h1>
    <p class="description">Invited as ${esc(preview.role)} · ${esc(preview.email)}</p>
    <label class="form-label">Display name</label><input class="field" id="inviteDisplayName">
    <label class="form-label">Username</label><input class="field" id="inviteUsername">
    <label class="form-label">Password</label><input class="field" id="invitePassword" type="password">
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="inviteAccept">Accept invitation</button></div>
  </div></div>`;
  el('inviteAccept').onclick=async()=>{
    try{
      const result=await api(`/api/invitations/${encodeURIComponent(token)}/accept`,{method:'POST',body:JSON.stringify({
        displayName:el('inviteDisplayName').value,username:el('inviteUsername').value,password:el('invitePassword').value
      })});
      state.token=result.token;localStorage.setItem(TOKEN_KEY,state.token);await loadWorkspace();showToast('Welcome');
    }catch(error){showToast(error.message,true)}
  };
}

function changeLabels(change){
  const labels=[];
  const status=String(change.status||'');
  if(status==='Draft')labels.push({text:'draft',tone:'slate'});
  else if(status==='Approved')labels.push({text:'approved',tone:'green'});
  else if(status==='Changes Requested')labels.push({text:'changes',tone:'amber'});
  else if(status==='Merged')labels.push({text:'merged',tone:'violet'});
  else if(status==='Closed')labels.push({text:'closed',tone:'slate'});
  else labels.push({text:'open',tone:'blue'});
  const title=`${change.title||''} ${change.sourceBranch||''}`.toLowerCase();
  if(/\bfix\b|\bbug\b/.test(title))labels.push({text:'bug',tone:'red'});
  else if(/\bui\b|\bfront/.test(title))labels.push({text:'ui',tone:'cyan'});
  else if(/\bapi\b|\bauth\b/.test(title))labels.push({text:'api',tone:'blue'});
  else if(/\bperf/.test(title))labels.push({text:'performance',tone:'green'});
  else if(change.sourceBranch&&change.sourceBranch!=='main')labels.push({text:'feature',tone:'violet'});
  return labels.slice(0,3);
}

function changeChecksSummary(change){
  const status=String(change.status||'');
  if(status==='Merged'||status==='Approved')return {tone:'ok',label:'All passed'};
  if(status==='Changes Requested')return {tone:'bad',label:'1 failed'};
  if(status==='Closed')return {tone:'muted',label:'Closed'};
  if(change.canMerge)return {tone:'ok',label:'All passed'};
  if(change.providerMergeable===false)return {tone:'bad',label:'1 failed'};
  return {tone:'pending',label:'Pending'};
}

function changeRow(change,options={}){
  if(options.compact){
    return `<article class="change-row" data-route="/changes/${change.id}"><span class="number">#${esc(change.externalNumber||change.externalId)}</span><div><h3>${esc(change.title)}</h3><p>${esc(change.author)} · ${esc(change.sourceBranch)} → ${esc(change.targetBranch)}</p></div><span class="status ${statusClass(change.status)}">${esc(change.status)}</span></article>`;
  }
  const number=change.externalNumber||change.externalId;
  const approvals=(change.reviews||[]).filter(r=>r.state==='Approved').length;
  const requested=Math.max((change.reviewers||[]).length,approvals,1);
  const checks=changeChecksSummary(change);
  const labels=changeLabels(change);
  const desc=(change.description||`${change.sourceBranch||'?'} → ${change.targetBranch||'?'}`).trim();
  const shortDesc=desc.length>110?`${desc.slice(0,107)}…`:desc;
  const reviewers=change.reviewers||[];
  const extra=Math.max(0,reviewers.length-3);
  const updatedLabel=options.closedColumn
    ?esc(relativeTime(change.closedAt||change.mergedAt||change.updatedAt||change.createdAt))
    :esc(relativeTime(change.updatedAt||change.createdAt));
  return `<article class="change-row pr-row" data-route="/changes/${change.id}">
    <div class="pr-title-cell">
      <span class="pr-icon ${statusClass(change.status)}" aria-hidden="true">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><circle cx="4" cy="4" r="2.2" stroke="currentColor" stroke-width="1.4"/><circle cx="12" cy="12" r="2.2" stroke="currentColor" stroke-width="1.4"/><path d="M4 6.2v3.1a2.7 2.7 0 0 0 2.7 2.7H9.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><path d="M12 9.8V6.9a2.7 2.7 0 0 0-2.7-2.7H6.2" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>
      </span>
      <div class="pr-title-copy">
        <h3><span class="pr-number">#${esc(number)}</span> ${esc(change.title)}</h3>
        <p>${esc(shortDesc)}</p>
      </div>
    </div>
    <div class="pr-author-cell">
      <span class="avatar">${esc(initials(change.author))}</span>
      <div><strong>${esc(change.author)}</strong><small>${esc(relativeTime(change.createdAt||change.updatedAt))}</small></div>
    </div>
    <div class="pr-labels-cell">${labels.map(l=>`<span class="pr-label ${l.tone}">${esc(l.text)}</span>`).join('')}</div>
    <div class="pr-assignees-cell">${reviewers.slice(0,3).map(r=>`<span class="avatar sm" title="${esc(r.name)}">${esc(initials(r.name))}</span>`).join('')}${extra?`<span class="avatar sm more">+${extra}</span>`:''}${reviewers.length?'':'<span class="avatar sm add" title="No assignees">＋</span>'}</div>
    <div class="pr-reviews-cell"><span class="pr-reviews-icon" aria-hidden="true">◎</span>${approvals}/${requested}</div>
    <div class="pr-checks-cell"><span class="pr-check ${checks.tone}">${checks.tone==='ok'?'✓':checks.tone==='bad'?'✕':'○'}</span> ${esc(checks.label)}</div>
    <div class="pr-updated-cell">${updatedLabel}</div>
    <div class="pr-actions-cell"><button type="button" class="icon-button ghost" data-route="/changes/${change.id}" aria-label="Open pull request">⋯</button></div>
  </article>`;
}

function changeTableHeader(closedColumn=false){
  return `<div class="pr-table-head">
    <span>Title</span><span>Author</span><span>Labels</span><span>Assignees</span><span>Reviews</span><span>Checks</span><span>${closedColumn?'Closed':'Updated'}</span><span></span>
  </div>`;
}

function renderChanges(){
  const project=state.context?.project;
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Pull Requests`);
  if(!state.changesTab)state.changesTab='open';
  const counts={
    open:state.changes.filter(c=>['Open','Approved','Changes Requested'].includes(c.status)).length,
    draft:state.changes.filter(c=>c.status==='Draft').length,
    merged:state.changes.filter(c=>c.status==='Merged').length,
    closed:state.changes.filter(c=>c.status==='Closed').length
  };
  const labelOptions=[...new Set(state.changes.flatMap(c=>changeLabels(c).map(l=>l.text)))].sort();
  el('content').innerHTML=`
  <section class="project-hero pr-hero">
    <div class="project-hero-top">
      <div class="project-identity">
        <div class="project-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><circle cx="6" cy="6" r="3" stroke="currentColor" stroke-width="1.8"/><circle cx="18" cy="18" r="3" stroke="currentColor" stroke-width="1.8"/><path d="M6 9v4.5A4.5 4.5 0 0 0 10.5 18H15" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/><path d="M18 15v-4.5A4.5 4.5 0 0 0 13.5 6H9" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </div>
        <div>
          <h1>Pull Requests <span class="pill visibility-pill">Public</span></h1>
          <p class="project-desc">Review and collaborate on changes to improve your codebase.</p>
        </div>
      </div>
      <div class="header-actions">
        <button class="button" id="refreshChanges">Refresh</button>
        <button class="button" id="discoverButton">Discover</button>
        <button class="button primary" id="importButton">＋ New pull request</button>
      </div>
    </div>
    <div class="pr-status-tabs" role="tablist">
      <button type="button" class="pr-status-tab ${state.changesTab==='open'?'active':''}" data-changes-tab="open">Open <span class="count">${counts.open}</span></button>
      <button type="button" class="pr-status-tab ${state.changesTab==='draft'?'active':''}" data-changes-tab="draft">Drafts <span class="count">${counts.draft}</span></button>
      <button type="button" class="pr-status-tab ${state.changesTab==='merged'?'active':''}" data-changes-tab="merged">Merged <span class="count">${counts.merged}</span></button>
      <button type="button" class="pr-status-tab ${state.changesTab==='closed'?'active':''}" data-changes-tab="closed">Closed <span class="count">${counts.closed}</span></button>
    </div>
  </section>

  <div class="pr-filterbar filterbar">
    <input class="field compact pr-search" id="changeSearch" placeholder="Search pull requests…">
    <select class="field compact" id="statusFilter" aria-label="Status">
      <option value="">All statuses</option>
      ${['Open','Draft','Approved','Changes Requested','Merged','Closed'].map(value=>`<option>${value}</option>`).join('')}
    </select>
    <input class="field compact" id="authorFilter" placeholder="Author">
    <select class="field compact" id="labelFilter" aria-label="Label">
      <option value="">Label</option>
      ${labelOptions.map(l=>`<option value="${esc(l)}">${esc(l)}</option>`).join('')}
    </select>
    <input class="field compact" id="reviewerFilter" placeholder="Assignee">
    <select class="field compact" id="changeSort" aria-label="Sort">
      <option value="updated">Sort: Recently updated</option>
      <option value="created">Sort: Newest</option>
      <option value="title">Sort: Title</option>
    </select>
  </div>

  <div class="card pr-table-card">
    <div class="card-header"><h2 id="changeListHeading">Open pull requests</h2><span class="muted-hint" id="changeListHint">${counts.open} open pull requests</span></div>
    ${changeTableHeader(false)}
    <div id="changeList"></div>
  </div>

  <div class="card pr-table-card" id="recentClosedCard" style="margin-top:16px">
    <div class="card-header"><div><h2>Recently closed</h2><p class="muted-hint" style="margin:4px 0 0">Showing 5 most recent</p></div><button type="button" class="button text" id="viewAllClosed">View all closed</button></div>
    ${changeTableHeader(true)}
    <div id="recentClosedList"></div>
  </div>`;

  el('importButton').onclick=openImport;
  el('discoverButton').onclick=openDiscover;
  el('refreshChanges').onclick=async()=>{state.changes=await api('/api/review/changes');renderChanges();showToast('Changes refreshed')};
  el('viewAllClosed').onclick=()=>{state.changesTab='closed';el('statusFilter').value='Closed';syncChangesTabUi();applyChangeFilters()};
  document.querySelectorAll('[data-changes-tab]').forEach(button=>button.onclick=()=>{
    state.changesTab=button.dataset.changesTab;
    if(state.changesTab==='open')el('statusFilter').value='';
    else if(state.changesTab==='draft')el('statusFilter').value='Draft';
    else if(state.changesTab==='merged')el('statusFilter').value='Merged';
    else if(state.changesTab==='closed')el('statusFilter').value='Closed';
    syncChangesTabUi();
    applyChangeFilters();
  });
  const apply=()=>applyChangeFilters();
  el('statusFilter').onchange=()=>{
    const v=el('statusFilter').value.toLowerCase();
    if(v==='draft')state.changesTab='draft';
    else if(v==='merged')state.changesTab='merged';
    else if(v==='closed')state.changesTab='closed';
    else state.changesTab='open';
    syncChangesTabUi();
    apply();
  };
  el('authorFilter').oninput=apply;
  el('reviewerFilter').oninput=apply;
  el('labelFilter').onchange=apply;
  el('changeSearch').oninput=apply;
  el('changeSort').onchange=apply;
  applyChangeFilters();
}

function syncChangesTabUi(){
  document.querySelectorAll('[data-changes-tab]').forEach(tab=>tab.classList.toggle('active',tab.dataset.changesTab===state.changesTab));
}

function applyChangeFilters(){
  const status=(el('statusFilter')?.value||'').toLowerCase();
  const author=(el('authorFilter')?.value||'').toLowerCase();
  const reviewer=(el('reviewerFilter')?.value||'').toLowerCase();
  const label=(el('labelFilter')?.value||'').toLowerCase();
  const search=(el('changeSearch')?.value||'').toLowerCase();
  const sort=el('changeSort')?.value||'updated';
  const matches=c=>{
    if(status){
      if(String(c.status).toLowerCase()!==status)return false;
    }else if(state.changesTab==='open'){
      if(!['Open','Approved','Changes Requested'].includes(c.status))return false;
    }else if(state.changesTab==='draft'){
      if(c.status!=='Draft')return false;
    }else if(state.changesTab==='merged'){
      if(c.status!=='Merged')return false;
    }else if(state.changesTab==='closed'){
      if(c.status!=='Closed')return false;
    }
    if(author&&!(c.author||'').toLowerCase().includes(author))return false;
    if(reviewer&&!(c.reviewers||[]).some(r=>(r.name||'').toLowerCase().includes(reviewer)))return false;
    if(label&&!changeLabels(c).some(l=>l.text.toLowerCase()===label))return false;
    if(search){
      const hay=`${c.title||''} ${c.description||''} ${c.author||''} ${c.externalNumber||''}`.toLowerCase();
      if(!hay.includes(search))return false;
    }
    return true;
  };
  const sorted=(items)=>items.slice().sort((a,b)=>{
    if(sort==='title')return String(a.title||'').localeCompare(String(b.title||''));
    if(sort==='created')return new Date(b.createdAt||0)-new Date(a.createdAt||0);
    return new Date(b.updatedAt||b.createdAt||0)-new Date(a.updatedAt||a.createdAt||0);
  });
  const items=sorted(state.changes.filter(matches));
  const list=el('changeList');
  if(!list)return;
  const closedView=status==='closed'||state.changesTab==='closed';
  list.innerHTML=items.length?items.map(c=>changeRow(c,{closedColumn:closedView})).join(''):'<div class="empty">No matching pull requests.</div>';
  const heading=el('changeListHeading');const hint=el('changeListHint');
  const title=status==='draft'||state.changesTab==='draft'?'Draft pull requests'
    :status==='merged'||state.changesTab==='merged'?'Merged pull requests'
    :status==='closed'||state.changesTab==='closed'?'Closed pull requests'
    :'Open pull requests';
  if(heading)heading.textContent=title;
  if(hint)hint.textContent=`${items.length} ${items.length===1?'pull request':'pull requests'}`;

  const closedCard=el('recentClosedCard');
  const showRecent=!status&&state.changesTab==='open'&&!author&&!reviewer&&!label&&!search;
  if(closedCard){
    closedCard.style.display=showRecent?'':'none';
    if(showRecent){
      const recent=sorted(state.changes.filter(c=>c.status==='Closed'||c.status==='Merged')).slice(0,5);
      el('recentClosedList').innerHTML=recent.length?recent.map(c=>changeRow(c,{closedColumn:true})).join(''):'<div class="empty small">No recently closed pull requests.</div>';
    }
  }
}

function renderQueue(){
  crumbs(projectCrumb('Review <span>/</span> Review queue'));
  const waiting=state.changes.filter(c=>c.reviewers.some(r=>r.name===actor()&&r.status==='Requested'));
  const mine=state.changes.filter(c=>c.author===actor());
  const reviewed=state.changes.filter(c=>c.reviews.some(r=>r.reviewer===actor())).slice().sort((a,b)=>new Date(b.updatedAt)-new Date(a.updatedAt));
  const section=(title,items)=>`<div class="card queue-section"><div class="card-header"><h2>${title}</h2><span class="pill">${items.length}</span></div>${items.length?items.map(c=>changeRow(c,{compact:true})).join(''):'<div class="empty small">Nothing here.</div>'}</div>`;
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Review queue</h1><p>Your review work, without CI or deployment noise.</p></div></div>${section('Waiting for me',waiting)}${section('Authored by me',mine)}${section('Recently reviewed',reviewed)}`;
}

function tabs(){return state.modules.flatMap(module=>module.resourceTabs||[]).filter(tab=>tab.resource==='change').sort((a,b)=>a.order-b.order)}

function renderChange(){
  const c=state.change;
  crumbs(projectCrumb(`Review <span>/</span> Change #${esc(c.externalNumber||c.externalId)}`));
  el('content').innerHTML=`<div class="change-header"><div>
    <div class="eyebrow"><span class="repo-mark">⑂</span>${esc(c.repository.owner)}/${esc(c.repository.name)} · <a href="${esc(c.externalUrl)}" target="_blank" rel="noreferrer">GitHub #${esc(c.externalNumber)}</a></div>
    <h1>${esc(c.title)}</h1>
    <div class="change-meta"><span class="status ${statusClass(c.status)}">${esc(c.status)}</span><span>${esc(c.author)} wants to merge</span><span class="branch">${esc(c.sourceBranch)}</span><span>into</span><span class="branch">${esc(c.targetBranch)}</span><span>${c.files.length} files</span><span class="additions">+${c.files.reduce((n,f)=>n+f.additions,0)}</span><span class="deletions">−${c.files.reduce((n,f)=>n+f.deletions,0)}</span></div>
  </div>
  <div class="header-actions">
    <button class="button" id="refreshChange">↻ Refresh</button>
    <button class="button" id="reviewButton" ${['Merged','Closed'].includes(c.status)?'disabled':''}>Review changes</button>
    <button class="button primary" id="mergeButton" ${c.canMerge?'':'disabled'}>Merge</button>
    <button class="button danger" id="closeButton" ${['Merged','Closed'].includes(c.status)?'disabled':''}>Close</button>
  </div></div>
  <div class="tabbar" role="tablist">${tabs().map(tab=>`<button class="tab ${state.tab===tab.id?'active':''}" data-tab="${tab.id}" role="tab">${esc(tab.label)}${tab.id==='discussion'?`<span class="count">${c.comments.length}</span>`:''}</button>`).join('')}</div>
  <div id="tabContent"></div>`;
  document.querySelectorAll('[data-tab]').forEach(button=>button.onclick=async()=>{state.tab=button.dataset.tab;renderChange()});
  el('refreshChange').onclick=()=>refreshActiveChange(true);
  el('reviewButton').onclick=openReview;
  el('mergeButton').onclick=openMergeConfirm;
  el('closeButton').onclick=()=>mutate(`/api/review/changes/${c.id}/close`,{},'Change closed');
  renderTab();
}

async function renderTab(){
  const c=state.change,target=el('tabContent');
  if(state.tab==='overview'){target.innerHTML=overviewTab(c);enhanceOverviewChecks(c);return}
  else if(state.tab==='files'){target.innerHTML=filesTab(c);bindFileDiffs(c)}
  else if(state.tab==='discussion'){target.innerHTML=discussionTab(c);bindDiscussion(c)}
  else if(state.tab==='reviewers'){target.innerHTML=reviewersTab(c);const reviewer=el('reviewerButton');if(reviewer)reviewer.onclick=openReviewer}
  else if(state.tab==='checks'){
    const extension=tabs().find(tab=>tab.id==='checks');
    if(!extension){target.innerHTML='<div class="empty">Checks extension is not enabled.</div>';return}
    const result=await api(extension.dataEndpoint.replace('{id}',c.id));
    target.innerHTML=`<div class="card"><div class="card-body">${result.checks.map(check=>{
      const icon=checkIcon(check.status);
      const link=check.detailsUrl?`<a href="${esc(check.detailsUrl)}">${esc(check.detailsUrl.replace(/^#/,'')||'details')}</a>`:'';
      return `<div class="check"><span class="run-status ${statusClass(check.status)}">${icon}</span><span><strong>${esc(check.name)}</strong><small>${esc(check.summary||check.provider||'')} · ${esc(check.duration||'—')}${link?` · ${link}`:''}</small></span><span class="check-state ${statusClass(check.status)}">${esc(check.status)}</span></div>`;
    }).join('')||'No checks published.'}</div></div>`;
  }
}

function checkIcon(status){
  const s=String(status||'').toLowerCase();
  if(s==='passed'||s==='succeeded')return '✓';
  if(s==='failed'||s==='lost')return '✗';
  if(s==='running')return '●';
  return '○';
}

function overviewTab(c){
  const p=c.mergePolicy;
  const required=(p.requiredChecks||[]).map(check=>`<div class="requirement ${check.satisfied?'met':''}">${check.satisfied?'✓':'○'} Check: ${esc(check.name)} <small>(${esc(check.status)})</small></div>`).join('');
  return `<div class="panel-grid"><div>
    <div class="card"><div class="card-header"><h2>Description</h2><span class="pill">Synced ${new Date(c.lastSynchronizedAt).toLocaleTimeString()}</span></div><div class="card-body"><p class="description">${esc(c.description)||'No description provided.'}</p></div></div>
    <div class="card" style="margin-top:18px"><div class="card-header"><h2>Activity</h2></div><div class="card-body">${c.activity.slice().reverse().map(item=>`<div class="timeline-item"><span class="event-icon">◌</span><div class="timeline-copy"><strong>${esc(item.actor)}</strong> ${esc(item.detail)}<small>${new Date(item.createdAt).toLocaleString()}</small></div></div>`).join('')}</div></div>
  </div>
  <aside><div class="card"><div class="card-header"><h2>Merge requirements</h2></div><div class="card-body">
    <div class="requirement ${p.hasApproval?'met':''}">${p.hasApproval?'✓':'○'} 1 approval</div>
    <div class="requirement ${!p.hasBlockingReview?'met':''}">${!p.hasBlockingReview?'✓':'○'} No active changes requested</div>
    <div class="requirement ${p.providerMergeable?'met':''}">${p.providerMergeable?'✓':'○'} GitHub reports mergeable</div>
    ${required}
    <div id="overviewChecks"></div>
    <div class="policy-box">${c.canMerge?'Ready to merge.':'Requirements must pass before merge.'}</div>
  </div></div></aside></div>`;
}

async function enhanceOverviewChecks(c){
  const host=el('overviewChecks');
  if(!host)return;
  const extension=tabs().find(tab=>tab.id==='checks');
  if(!extension)return;
  try{
    const result=await api(extension.dataEndpoint.replace('{id}',c.id));
    if(!result.checks?.length)return;
    host.innerHTML=`<div style="margin-top:12px;padding-top:10px;border-top:1px solid var(--line)"><strong style="font-size:.78rem">Checks</strong>${result.checks.map(check=>`<div class="requirement ${String(check.status).toLowerCase()==='passed'?'met':''}">${checkIcon(check.status)} ${esc(check.name)} <small>${esc(check.duration||'')}</small></div>`).join('')}</div>`;
  }catch{/* optional */}
}

function filesTab(c){
  return `<div class="file-summary">${c.files.map(file=>`<button class="file-chip" data-jump-file="${esc(file.path)}">${esc(file.path)} <span class="additions">+${file.additions}</span> <span class="deletions">−${file.deletions}</span></button>`).join('')}</div>
  <div id="diffFiles">${c.files.map(file=>{
    const open=!!state.expandedFiles[file.path];
    return `<article class="card diff-file" data-file-card="${esc(file.path)}">
      <button class="diff-meta" data-toggle-file="${esc(file.path)}" style="width:100%;cursor:pointer;background:transparent;color:inherit;text-align:left">
        <strong>${open?'▾':'▸'} ${esc(file.path)}</strong>
        <span>${esc(file.status)} · <span class="additions">+${file.additions}</span> <span class="deletions">−${file.deletions}</span></span>
      </button>
      <div class="diff-body">${open?`<pre class="diff">${diffLines(state.expandedFiles[file.path])}</pre>`:'<div class="empty small">Collapsed — expand to load the file diff.</div>'}</div>
    </article>`;
  }).join('')}</div>`;
}

function bindFileDiffs(c){
  document.querySelectorAll('[data-jump-file]').forEach(button=>button.onclick=()=>{const card=document.querySelector(`[data-file-card="${CSS.escape(button.dataset.jumpFile)}"]`);card?.scrollIntoView({behavior:'smooth',block:'start'});if(!state.expandedFiles[button.dataset.jumpFile])toggleFileDiff(c,button.dataset.jumpFile)});
  document.querySelectorAll('[data-toggle-file]').forEach(button=>button.onclick=()=>toggleFileDiff(c,button.dataset.toggleFile));
  document.querySelectorAll('[data-inline]').forEach(button=>button.onclick=()=>openInlineComment(button.dataset.file,Number(button.dataset.line)));
}

async function toggleFileDiff(c,path){
  if(state.expandedFiles[path]){delete state.expandedFiles[path];el('tabContent').innerHTML=filesTab(c);bindFileDiffs(c);return}
  try{
    const diff=await api(`/api/review/changes/${c.id}/diff/file?path=${encodeURIComponent(path)}`);
    const file=diff.files?.[0]||c.files.find(f=>f.path===path);
    if(!file)throw new Error('File diff was not found.');
    state.expandedFiles[path]=file;
    el('tabContent').innerHTML=filesTab(c);bindFileDiffs(c);
  }catch(error){showToast(error.message,true)}
}

function diffLines(file){
  if(!file?.patch)return '<span class="diff-line"><code>No patch available.</code></span>';
  let oldLine=0,newLine=0;
  return file.patch.split('\n').map(line=>{
    if(line.startsWith('@@')){const match=/-(\d+).*\+(\d+)/.exec(line);if(match){oldLine=Number(match[1])-1;newLine=Number(match[2])-1}}
    else if(line.startsWith('+'))newLine++;
    else if(line.startsWith('-'))oldLine++;
    else{oldLine++;newLine++}
    const n=line.startsWith('-')?oldLine:newLine;
    return `<span class="diff-line ${line.startsWith('+')?'diff-add':line.startsWith('-')?'diff-del':''}"><button class="line-comment" data-inline="1" data-file="${esc(file.path)}" data-line="${n}" title="Comment on line ${n}">＋</button><i>${line.startsWith('-')?oldLine:''}</i><i>${line.startsWith('+')||(!line.startsWith('-')&&!line.startsWith('@@'))?newLine:''}</i><code>${esc(line)}</code></span>`;
  }).join('');
}

function discussionTab(c){
  const roots=c.comments.filter(comment=>!comment.parentId);
  return `<div class="panel-grid"><div class="card"><div class="card-header"><h2>Discussion</h2></div><div class="card-body">
    ${roots.map(root=>thread(c,root)).join('')||'<div class="empty small">No discussion yet.</div>'}
    <div class="comment-box"><textarea id="commentText" aria-label="Add comment" placeholder="Leave a general comment…"></textarea><div class="comment-actions"><button class="button primary" id="commentButton">Comment</button></div></div>
  </div></div>
  <aside><div class="card"><div class="card-body"><strong>Platform-owned review history</strong><p class="description">Comments stay here even when GitHub state changes. Outdated inline comments are marked after new commits land.</p></div></div></aside></div>`;
}

function thread(c,root){
  const replies=c.comments.filter(comment=>comment.parentId===root.id);
  const own=root.author===actor();
  return `<div class="discussion ${root.resolved?'resolved':''} ${root.outdated?'outdated':''}">
    <div class="comment"><span class="avatar">${initials(root.author)}</span><div>
      <div class="comment-author"><strong>${esc(root.author)}</strong><small>${new Date(root.createdAt).toLocaleString()}${root.editedAt?' · edited':''}</small>${root.outdated?'<span class="pill">Outdated</span>':''}</div>
      <p id="comment-body-${root.id}">${esc(root.body)}</p>
      ${root.file?`<span class="inline-ref">${esc(root.file)}:${root.line} · ${esc(root.commitSha?.slice(0,7))}</span>`:''}
    </div></div>
    ${replies.map(reply=>{
      const replyOwn=reply.author===actor();
      return `<div class="comment reply ${reply.outdated?'outdated':''}"><span class="avatar">${initials(reply.author)}</span><div>
        <div class="comment-author"><strong>${esc(reply.author)}</strong><small>${new Date(reply.createdAt).toLocaleString()}${reply.editedAt?' · edited':''}</small>${reply.outdated?'<span class="pill">Outdated</span>':''}</div>
        <p id="comment-body-${reply.id}">${esc(reply.body)}</p>
        ${replyOwn?`<div class="discussion-actions"><button class="button text" data-edit-comment="${reply.id}">Edit</button><button class="button text" data-delete-comment="${reply.id}">Delete</button></div>`:''}
      </div></div>`;
    }).join('')}
    <div class="discussion-actions">
      <button class="button text" data-reply="${root.id}">Reply</button>
      <button class="button text" data-resolve="${root.discussionId}" data-action="${root.resolved?'reopen':'resolve'}">${root.resolved?'Reopen':'Resolve'}</button>
      ${own?`<button class="button text" data-edit-comment="${root.id}">Edit</button><button class="button text" data-delete-comment="${root.id}">Delete</button>`:''}
    </div>
  </div>`;
}

function bindDiscussion(c){
  document.querySelectorAll('[data-resolve]').forEach(button=>button.onclick=()=>mutate(`/api/review/changes/${c.id}/discussions/${button.dataset.resolve}/${button.dataset.action}`,{},button.dataset.action==='resolve'?'Discussion resolved':'Discussion reopened'));
  document.querySelectorAll('[data-reply]').forEach(button=>button.onclick=()=>openReply(button.dataset.reply));
  document.querySelectorAll('[data-edit-comment]').forEach(button=>button.onclick=()=>openEditComment(button.dataset.editComment));
  document.querySelectorAll('[data-delete-comment]').forEach(button=>button.onclick=()=>deleteComment(button.dataset.deleteComment));
  const add=el('commentButton');if(add)add.onclick=addComment;
}

function reviewersTab(c){
  return `<div class="panel-grid"><div class="card"><div class="card-header"><h2>Reviewers</h2><button class="button" id="reviewerButton">＋ Request reviewer</button></div><div class="card-body">
    ${c.reviewers.map(r=>`<div class="reviewer"><span class="avatar">${initials(r.name)}</span><span><strong>${esc(r.name)}</strong><small>${esc(r.status)}</small></span><span class="pill">${esc(r.status)}</span></div>`).join('')||'<div class="empty small">No reviewers assigned.</div>'}
  </div></div>
  <aside><div class="card"><div class="card-body"><strong>Community policy</strong><p class="description">One approval and no active changes-requested review.</p></div></div></aside></div>`;
}

function formatBytes(n){
  const value=Number(n)||0;
  if(value<1024)return `${value} B`;
  if(value<1024*1024)return `${(value/1024).toFixed(1)} KB`;
  return `${(value/(1024*1024)).toFixed(1)} MB`;
}

function inferLanguages(entries){
  const weights={};
  (entries||[]).filter(e=>e.kind==='file').forEach(entry=>{
    const name=entry.name||'';
    const ext=(name.includes('.')?name.split('.').pop():'').toLowerCase();
    const map={ts:'TypeScript',tsx:'TypeScript',js:'JavaScript',jsx:'JavaScript',cs:'C#',css:'CSS',scss:'CSS',html:'HTML',json:'JSON',yml:'YAML',yaml:'YAML',md:'Markdown',py:'Python',go:'Go',rs:'Rust',dockerfile:'Docker'};
    const lang=name.toLowerCase()==='dockerfile'?'Docker':(map[ext]||'Other');
    weights[lang]=(weights[lang]||0)+(entry.size||1);
  });
  const total=Object.values(weights).reduce((a,b)=>a+b,0)||1;
  const colors={TypeScript:'#3178c6',JavaScript:'#f7df1e',CSS:'#563d7c','C#':'#512bd4',HTML:'#e34c26',JSON:'#292929',YAML:'#cb171e',Markdown:'#083fa1',Python:'#3572a5',Go:'#00add8',Rust:'#dea584',Docker:'#2496ed',Other:'#94a3b8'};
  return Object.entries(weights).map(([name,size])=>({name,pct:Math.round((size/total)*1000)/10,color:colors[name]||'#94a3b8'})).sort((a,b)=>b.pct-a.pct);
}

function simpleMarkdown(text){
  return esc(text||'')
    .replace(/^### (.*)$/gm,'<h3>$1</h3>')
    .replace(/^## (.*)$/gm,'<h2>$1</h2>')
    .replace(/^# (.*)$/gm,'<h1>$1</h1>')
    .replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>')
    .replace(/`([^`]+)`/g,'<code>$1</code>')
    .replace(/\n\n/g,'</p><p>')
    .replace(/\n/g,'<br>');
}

function openCloneMenu(){
  if(!source())return showToast('Connect a repository first.',true);
  const url=source().url||`https://github.com/${source().repositoryId.owner}/${source().repositoryId.name}`;
  openModal(`<div class="modal-content"><h2>Clone repository</h2>
    <p class="description">${esc(source().repositoryId.owner)}/${esc(source().repositoryId.name)}</p>
    <label class="form-label">HTTPS</label><input class="field" id="cloneUrl" value="${esc(url)}" readonly>
    <div class="modal-actions"><button class="button" value="cancel">Close</button><button class="button primary" value="copy">Copy</button></div></div>`,async e=>{
    if(e.submitter?.value==='copy'){try{await navigator.clipboard.writeText(el('cloneUrl').value);showToast('URL copied')}catch{showToast('Could not copy',true)}}
  });
}

function fileBreadcrumbs(path,reference,repoName){
  const parts=path?path.split('/').filter(Boolean):[];
  let acc='';
  const rootLabel=esc(repoName||'root');
  const links=[`<button class="button text" data-route="/files?ref=${encodeURIComponent(reference)}">${rootLabel}</button>`];
  parts.forEach((part,index)=>{acc=acc?`${acc}/${part}`:part;const last=index===parts.length-1;links.push(last?`<span>${esc(part)}</span>`:`<button class="button text" data-route="/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(acc)}">${esc(part)}</button>`)});
  return links.join(' <span>/</span> ');
}

async function renderFiles(route){
  const project=state.context?.project;
  if(!source()){
    crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Repositories`);
    return renderNoSource();
  }
  const query=new URLSearchParams(route.split('?')[1]||'');
  const path=query.get('path')||'';
  const reference=query.get('ref')||source().defaultBranch;
  const repo=source().repositoryId;
  const repoName=repo.name;
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Repositories <span>/</span> ${esc(repoName)}`);

  const [tree,branches,commits,runs]=await Promise.all([
    api(`/api/source/repositories/${source().id}/tree?reference=${encodeURIComponent(reference)}&path=${encodeURIComponent(path)}`),
    api(`/api/source/repositories/${source().id}/branches`).catch(()=>[]),
    api(`/api/source/repositories/${source().id}/commits?branch=${encodeURIComponent(reference)}`).catch(()=>[]),
    hasModule('pipelines')?api('/api/pipelines/runs').catch(()=>[]):Promise.resolve([])
  ]);

  const head=commits[0]||null;
  const latestRun=(runs||[])[0]||null;
  const langs=inferLanguages(tree.entries);
  const entries=[...(tree.entries||[])].sort((a,b)=>{
    if(a.kind!==b.kind)return a.kind==='directory'?-1:1;
    return a.name.localeCompare(b.name);
  });
  const parentPath=path?path.split('/').slice(0,-1).join('/'):'';
  const tags=[repo.provider,'source',reference,hasModule('pipelines')?'ci':''].filter(Boolean);
  const about=project?.description||`Source repository ${repo.owner}/${repo.name}.`;

  let readmeHtml='';
  if(!path){
    const readmeEntry=entries.find(e=>e.kind==='file'&&/^readme(\.|$)/i.test(e.name));
    if(readmeEntry){
      try{
        const file=await api(`/api/source/repositories/${source().id}/file?reference=${encodeURIComponent(reference)}&path=${encodeURIComponent(readmeEntry.path)}`);
        if(file&&!file.isBinary&&file.content){
          readmeHtml=`<div class="card readme-card"><div class="card-header"><h2>${esc(readmeEntry.name)}</h2></div><div class="card-body readme-body"><div class="readme-title">${esc(repoName)}</div>
            <div class="readme-badges"><span class="pill visibility-pill">source connected</span>${latestRun?`<span class="pill ${['Succeeded','PartiallySucceeded'].includes(latestRun.status)?'visibility-pill':''}">build ${esc(latestRun.status)}</span>`:''}<span class="pill">branch ${esc(reference)}</span></div>
            <div class="readme-prose"><p>${simpleMarkdown(file.content.slice(0,4000))}</p></div></div></div>`;
        }
      }catch{/* optional */}
    }
  }

  el('content').innerHTML=`
  <section class="project-hero repo-hero">
    <div class="project-hero-top">
      <div class="project-identity">
        <div class="project-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><path d="M4 7.5h16M4 12h16M4 16.5h10" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/><rect x="3" y="4" width="18" height="16" rx="3" stroke="currentColor" stroke-width="1.8"/></svg>
        </div>
        <div>
          <h1>${esc(repoName)} <span class="pill visibility-pill">Public</span></h1>
          <p class="project-desc">${esc(about)}</p>
          <div class="tag-row">${tags.map(t=>`<span class="tag">${esc(t)}</span>`).join('')}</div>
        </div>
      </div>
      <div class="header-actions">
        <button class="button" id="cloneButton">&lt;&gt; Code</button>
        ${hasModule('pipelines')?'<button class="button" data-route="/pipelines">Run pipeline</button>':''}
        ${hasModule('review')?'<button class="button primary" data-route="/changes">＋ Create pull request</button>':''}
      </div>
    </div>
  </section>

  <div class="repo-layout">
    <div class="repo-main">
      ${head?`<div class="commit-bar">
        <span class="avatar">${esc(initials(head.author))}</span>
        <div class="commit-bar-copy">
          <strong>${esc(head.author)}</strong>
          <span class="commit-msg">${esc((head.message||'').split('\n')[0])}</span>
          <span class="commit-ok" title="Latest on ${esc(reference)}">✓</span>
          <code data-route="/commits/${esc(head.sha)}">${esc(head.sha.slice(0,7))}</code>
          <small>${esc(relativeTime(head.authoredAt))}</small>
        </div>
        <button class="button" data-route="/commits">View history</button>
      </div>`:''}

      <div class="card file-browser">
        <div class="file-toolbar">
          ${repoPickerHtml()}
          <select class="field compact" id="refPicker" aria-label="Branch"></select>
          <div class="file-path">${fileBreadcrumbs(path,reference,repoName)}</div>
          <div class="file-toolbar-actions">
            <button class="button compact" id="goToFile" type="button">Go to file…</button>
            <button class="button compact" data-route="/source-branches">Branches</button>
          </div>
        </div>
        <div class="file-table-head"><span>Name</span><span>Last commit</span><span>Updated</span></div>
        <div class="file-table-body">
          ${path?`<button type="button" class="file-row" data-up="${esc(parentPath)}"><span class="file-name"><span class="file-icon up">↰</span><strong>..</strong></span><span class="file-commit muted-hint">Parent directory</span><span class="file-updated"></span></button>`:''}
          ${entries.map(entry=>`<button type="button" class="file-row" data-path="${esc(entry.path)}" data-kind="${esc(entry.kind)}">
            <span class="file-name"><span class="file-icon ${entry.kind==='directory'?'dir':'file'}" aria-hidden="true">${entry.kind==='directory'?'▱':'≡'}</span><strong>${esc(entry.name)}</strong></span>
            <span class="file-commit">${entry.kind==='directory'?'—':esc(formatBytes(entry.size))}${entry.sha?` · <code>${esc(String(entry.sha).slice(0,7))}</code>`:''}</span>
            <span class="file-updated">${head?esc(relativeTime(head.authoredAt)):'—'}</span>
          </button>`).join('')||'<div class="empty small">This directory is empty.</div>'}
        </div>
      </div>
      ${readmeHtml}
    </div>

    <aside class="repo-aside">
      <div class="card"><div class="card-header"><h2>About</h2></div><div class="card-body">
        <p class="description">${esc(about)}</p>
        <div class="about-links">
          <a class="about-link" href="${esc(source().url||'#')}" target="_blank" rel="noreferrer">Repository on GitHub</a>
          <button type="button" class="about-link" data-route="/commits">Commit history</button>
          <button type="button" class="about-link" data-route="/source-branches">Branches</button>
          <button type="button" class="about-link" data-route="/settings">Source settings</button>
        </div>
        <div class="repo-stats">
          <div><strong>${esc(String((branches||[]).length))}</strong><span>branches</span></div>
          <div><strong>${esc(String((commits||[]).length))}</strong><span>recent commits</span></div>
          <div><strong>${esc(String(entries.length))}</strong><span>entries</span></div>
        </div>
      </div></div>
      <div class="card"><div class="card-header"><h2>Releases</h2></div><div class="card-body">
        <div class="release-row"><strong>${esc(reference)}</strong><span class="pill visibility-pill">Default</span></div>
        <p class="description" style="margin:8px 0 0">Branch tip ${head?`<code>${esc(head.sha.slice(0,7))}</code> · ${esc(relativeTime(head.authoredAt))}`:'unavailable'}.</p>
      </div></div>
      <div class="card"><div class="card-header"><h2>CI / CD</h2></div><div class="card-body">
        ${latestRun?`<button type="button" class="ci-row" data-route="/runs/${esc(latestRun.id)}">
          <span class="run-status ${statusClass(latestRun.status)}">${checkIcon(latestRun.status)}</span>
          <div><strong>${esc(latestRun.definitionName||'Pipeline')}</strong><small>#${esc(shortId(latestRun.id))} · ${esc(relativeTime(latestRun.completedAt||latestRun.startedAt))}</small></div>
        </button>`:`<div class="empty small">${hasModule('pipelines')?'No pipeline runs yet.':'Pipelines not enabled.'}</div>`}
      </div></div>
      <div class="card"><div class="card-header"><h2>Languages</h2></div><div class="card-body">
        ${langs.length?`<div class="lang-bar">${langs.map(l=>`<span style="width:${Math.max(l.pct,2)}%;background:${l.color}" title="${esc(l.name)} ${l.pct}%"></span>`).join('')}</div>
          <div class="lang-legend">${langs.slice(0,5).map(l=>`<div><span class="lang-dot" style="background:${l.color}"></span>${esc(l.name)} <strong>${l.pct}%</strong></div>`).join('')}</div>`:'<div class="empty small">No language signals in this folder.</div>'}
      </div></div>
    </aside>
  </div>`;

  el('refPicker').innerHTML=(branches||[]).map(branch=>`<option value="${esc(branch.name)}" ${branch.name===reference?'selected':''}>${esc(branch.name)}${branch.isDefault?' (default)':''}</option>`).join('')||`<option>${esc(reference)}</option>`;
  el('refPicker').onchange=()=>navigate(`/files?ref=${encodeURIComponent(el('refPicker').value)}`);
  bindRepoPicker(()=>renderFiles(route));
  el('cloneButton').onclick=openCloneMenu;
  el('goToFile').onclick=()=>{
    openModal(`<div class="modal-content"><h2>Go to file</h2>
      <label class="form-label">Path</label><input class="field" id="goFilePath" placeholder="src/Program.cs" value="${esc(path)}">
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Open</button></div></div>`,e=>{
      if(e.submitter?.value!=='submit')return;
      const target=(el('goFilePath').value||'').trim();
      if(!target)return;
      const hit=entries.find(x=>x.path===target||x.name===target);
      if(hit?.kind==='directory')navigate(`/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(hit.path)}`);
      else renderSourceFile(hit?.path||target,reference);
    });
  };
  document.querySelectorAll('.file-row[data-path]').forEach(button=>button.onclick=()=>button.dataset.kind==='directory'
    ?navigate(`/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(button.dataset.path)}`)
    :renderSourceFile(button.dataset.path,reference));
  const up=document.querySelector('[data-up]');
  if(up)up.onclick=()=>navigate(`/files?ref=${encodeURIComponent(reference)}${up.dataset.up?`&path=${encodeURIComponent(up.dataset.up)}`:''}`);
}

async function renderSourceFile(path,reference){
  const project=state.context?.project;
  const repoName=source()?.repositoryId?.name||'repository';
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Repositories <span>/</span> ${esc(repoName)} <span>/</span> ${esc(path)}`);
  const file=await api(`/api/source/repositories/${source().id}/file?reference=${encodeURIComponent(reference)}&path=${encodeURIComponent(path)}`);
  el('content').innerHTML=`
  <section class="project-hero repo-hero">
    <div class="project-hero-top">
      <div class="project-identity">
        <div class="project-icon" aria-hidden="true">≡</div>
        <div>
          <h1 class="file-title">${esc(path.split('/').pop())}</h1>
          <p class="project-desc">${esc(reference)} · ${formatBytes(file.size)} · <code>${esc(file.sha.slice(0,7))}</code></p>
          <div class="file-path" style="margin-top:10px">${fileBreadcrumbs(path,reference,repoName)}</div>
        </div>
      </div>
      <div class="header-actions">
        <button class="button" id="copyPath">Copy path</button>
        <button class="button" data-route="/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(path.split('/').slice(0,-1).join('/'))}">Back</button>
      </div>
    </div>
  </section>
  <div class="card code-view">${file.isBinary?'<div class="empty">Binary files are not rendered inline.</div>':`<pre>${(file.content||'').split('\n').map((line,index)=>`<span><i>${index+1}</i><code>${esc(line)}</code></span>`).join('')}</pre>`}</div>`;
  el('copyPath').onclick=async()=>{try{await navigator.clipboard.writeText(path);showToast('Path copied')}catch{showToast('Could not copy path',true)}};
}

async function renderCommits(){
  crumbs(projectCrumb('Code <span>/</span> Commits'));
  if(!source())return renderNoSource();
  const commits=await api(`/api/source/repositories/${source().id}/commits?branch=${encodeURIComponent(source().defaultBranch)}`);
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Commits</h1><p>${esc(source().defaultBranch)} · GitHub source of truth</p></div></div>
    ${repoPickerHtml()}
    <div class="card">${commits.map(c=>`<button class="commit-row" data-route="/commits/${esc(c.sha)}" style="width:100%;border:0;background:transparent;cursor:pointer;text-align:left">
      <code>${esc(c.sha.slice(0,7))}</code><div><strong>${esc(c.message.split('\n')[0])}</strong><small>${esc(c.author)} · ${new Date(c.authoredAt).toLocaleString()}</small></div>
    </button>`).join('')}</div>`;
  bindRepoPicker(()=>renderCommits());
}

async function renderCommitDetail(sha){
  crumbs(projectCrumb(`Code <span>/</span> Commit ${esc(sha.slice(0,7))}`));
  if(!source())return renderNoSource();
  const [commit,diff]=await Promise.all([
    api(`/api/source/repositories/${source().id}/commits/${encodeURIComponent(sha)}`),
    api(`/api/source/repositories/${source().id}/compare?base=${encodeURIComponent(sha+'^')}&head=${encodeURIComponent(sha)}`).catch(()=>null)
  ]);
  el('content').innerHTML=`<div class="list-page-header"><div>
    <h1>${esc(commit.message.split('\n')[0])}</h1>
    <p><code>${esc(commit.sha)}</code> · ${esc(commit.author)} · ${new Date(commit.authoredAt).toLocaleString()}</p>
  </div><button class="button" data-route="/commits">Back to commits</button></div>
  ${commit.message.includes('\n')?`<div class="card" style="margin-bottom:18px"><div class="card-body"><p class="description">${esc(commit.message)}</p></div></div>`:''}
  <div class="card"><div class="card-header"><h2>Files changed</h2><span class="pill">${diff?diff.files.length:'—'}</span></div>
    ${diff?diff.files.map(file=>`<article class="diff-file"><div class="diff-meta"><strong>${esc(file.path)}</strong><span>${esc(file.status)} · <span class="additions">+${file.additions}</span> <span class="deletions">−${file.deletions}</span></span></div><pre class="diff">${diffLines(file)}</pre></article>`).join('')||'<div class="empty small">No file changes in this commit.</div>':'<div class="empty small">Could not load commit comparison.</div>'}
  </div>`;
}

async function renderBranches(){
  crumbs(projectCrumb('Code <span>/</span> Branches'));
  if(!source())return renderNoSource();
  const branches=await api(`/api/source/repositories/${source().id}/branches`);
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Branches</h1><p>Create a pull request without changing your Git workflow.</p></div></div>
    ${repoPickerHtml()}
    <div class="card">${branches.map(branch=>{
      const skip=branch.isDefault||hasOpenChangeForBranch(branch.name);
      return `<div class="branch-row"><div><strong>${esc(branch.name)} ${branch.isDefault?'<span class="pill">default</span>':''}${hasOpenChangeForBranch(branch.name)&&!branch.isDefault?'<span class="pill">open PR</span>':''}</strong>
        <small>${esc(branch.author)} · ${new Date(branch.updatedAt).toLocaleString()} · ${esc(branch.headSha.slice(0,7))}${branch.headMessage?` · ${esc(branch.headMessage)}`:''}</small></div>
        ${skip?'':`<button class="button" data-create-change="${esc(branch.name)}">Create pull request</button>`}
      </div>`;
    }).join('')}</div>`;
  bindRepoPicker(()=>renderBranches());
  document.querySelectorAll('[data-create-change]').forEach(button=>button.onclick=()=>openCreateChange(button.dataset.createChange));
}

function renderNoSource(){el('content').innerHTML='<div class="empty"><h2>No source repository connected</h2><p>Connect the project repository in Settings.</p><button class="button primary" data-route="/settings">Open settings</button></div>'}

function orgSettingsSections(){
  const sections=[
    {group:'General',items:[
      {id:'general',route:'/organisation/settings/general',label:'Overview'},
      {id:'projects',route:'/organisation/settings/projects',label:'Projects'},
      {id:'users',route:'/organisation/settings/users',label:'Users & Groups'}
    ]},
    {group:'Security',items:[
      {id:'security-auth',route:'/organisation/settings/security',label:'Authentication'},
      {id:'security-perms',route:'/organisation/settings/permissions',label:'Permissions'}
    ]},
    {group:'Platform',items:[
      {id:'license',route:'/organisation/settings/license',label:'License'},
      {id:'modules',route:'/organisation/settings/modules',label:'Modules'},
      {id:'connectors',route:'/organisation/settings/connectors',label:'Connectors'},
      {id:'audit',route:'/organisation/settings/audit',label:'Auditing'}
    ]}
  ];
  if(hasModule('pipelines')){
    sections.push({group:'Build',items:[
      {id:'build',route:'/organisation/settings/build',label:'Agent pools / runners'}
    ]});
  }
  return sections;
}

function projectSettingsSections(){
  const sections=[
    {group:'General',items:[
      {id:'general',route:'/settings/general',label:'Overview'}
    ]},
    {group:'Access',items:[
      {id:'members',route:'/settings/members',label:'Members & Teams'},
      {id:'roles',route:'/settings/roles',label:'Roles'},
      {id:'permissions',route:'/settings/permissions',label:'Permissions'}
    ]},
    {group:'Repositories',items:[
      {id:'repositories',route:'/settings/repositories',label:'Repositories'}
    ]},
    {group:'Modules',items:[
      {id:'modules',route:'/settings/modules',label:'Modules'}
    ]}
  ];
  if(hasModule('review')){
    sections.push({group:'Review',items:[
      {id:'review',route:'/settings/review',label:'Policies'}
    ]});
  }
  return sections;
}

function settingsNavHtml(kind,active){
  const sections=kind==='org'?orgSettingsSections():projectSettingsSections();
  return sections.map(section=>`
    <p class="settings-nav-group">${esc(section.group)}</p>
    ${section.items.map(item=>`<button type="button" class="settings-nav-item${item.route===active||active.startsWith(item.route+'/')?' active':''}" data-route="${esc(item.route)}">${esc(item.label)}</button>`).join('')}
  `).join('');
}

function renderSettingsShell(kind,active,title,description,bodyHtml){
  const crumb=kind==='org'
    ?orgCrumb(`Settings <span>/</span> ${esc(title)}`)
    :projectCrumb(`Settings <span>/</span> ${esc(title)}`);
  crumbs(crumb);
  const heading=kind==='org'?title:'Project settings';
  el('content').innerHTML=`
  <div class="list-page-header"><div>
    <h1>${esc(heading)}</h1>
    <p class="description">${esc(description)}</p>
  </div></div>
  <div class="settings-layout">
    <aside class="settings-nav" aria-label="${kind==='org'?'Organisation':'Project'} settings">
      ${settingsNavHtml(kind,active)}
    </aside>
    <div class="settings-main">${bodyHtml}</div>
  </div>`;
  setActiveNav(state.route);
}

async function renderOrgSettings(route){
  const path=normalizeRoute(route);
  const section=path.replace('/organisation/settings/','').split('/')[0]||'general';
  if(section==='general')return await renderOrgGeneralSettings();
  if(section==='projects')return await renderOrgProjectsSettings();
  if(section==='users')return await renderOrgUsersSettings();
  if(section==='security')return await renderOrgSecuritySettings();
  if(section==='permissions')return await renderOrgPermissionsSettings();
  if(section==='license')return await renderLicensing();
  if(section==='modules')return await renderModules();
  if(section==='connectors')return await renderConnectors();
  if(section==='audit')return await renderAudit();
  if(section==='build')return await renderOrgBuildSettings();
  return await renderOrgGeneralSettings();
}

async function renderProjectSettings(route){
  const path=normalizeRoute(route);
  const section=path.replace('/settings/','').split('/')[0]||'general';
  if(section==='general')return await renderProjectGeneralSettings();
  if(section==='members')return await renderProjectMembersSettings();
  if(section==='roles')return await renderProjectRolesSettings();
  if(section==='permissions')return await renderProjectPermissionsSettings();
  if(section==='modules')return await renderProjectModulesSettings();
  if(section==='repositories')return await renderProjectRepositoriesSettings();
  if(section==='review')return await renderProjectReviewSettings();
  if(section==='build')return await renderProjectBuildSettings();
  return await renderProjectGeneralSettings();
}

async function renderOrgGeneralSettings(){
  const org=await api('/api/organisation');
  renderSettingsShell('org','/organisation/settings/general','Overview','Organisation profile and defaults.',`
    <div class="card"><div class="card-header"><h2>Organisation profile</h2></div>
    <div class="card-body settings-form">
      <label class="form-label" for="orgName">Name</label>
      <input class="field" id="orgName" value="${esc(org.name||'')}">
      <label class="form-label" for="orgDescription">Description</label>
      <textarea class="field" id="orgDescription" rows="3">${esc(org.description||'')}</textarea>
      <div class="modal-actions" style="margin-top:16px">
        <button class="button primary" id="orgSave">Save changes</button>
      </div>
    </div></div>`);
  el('orgSave').onclick=async()=>{
    try{
      const updated=await api('/api/organisation',{method:'PATCH',body:JSON.stringify({
        name:el('orgName').value,description:el('orgDescription').value
      })});
      if(state.context?.organisation){
        state.context.organisation.name=updated.name;
        state.context.organisation.description=updated.description;
      }
      el('orgLabel').textContent=updated.name;
      showToast('Organisation updated');
      await renderOrgGeneralSettings();
    }catch(error){showToast(error.message,true)}
  };
}

async function renderOrgProjectsSettings(){
  const projects=await api('/api/projects');
  state.projects=projects;
  renderSettingsShell('org','/organisation/settings/projects','Projects','Create, open, and remove projects.',`
    <div class="card"><div class="card-header"><h2>Projects</h2>
      <button class="button primary" id="orgProjectsNew">＋ New project</button>
    </div>
    <div class="card-body">
      ${projects.map(p=>`<div class="module-card">
        <span class="module-logo">${esc((p.name||'?')[0].toUpperCase())}</span>
        <div><h3>${esc(p.name)}</h3><p>/${esc(p.slug)} · ${esc(p.visibility||'Private')} · ${esc(p.repositoryMode==='MultiRepository'?'Multi-repo':'Single repo')}</p></div>
        <div class="settings-row-actions">
          <button class="button" data-open-project="${esc(p.id)}">Open</button>
          <button class="button danger" data-delete-project="${esc(p.id)}">Delete</button>
        </div>
      </div>`).join('')||'<div class="empty small">No projects yet.</div>'}
    </div></div>`);
  el('orgProjectsNew').onclick=()=>openCreateProject();
  document.querySelectorAll('[data-open-project]').forEach(btn=>btn.onclick=()=>openProject(btn.dataset.openProject));
  document.querySelectorAll('[data-delete-project]').forEach(btn=>btn.onclick=async()=>{
    if(!confirm('Delete this project? This cannot be undone.'))return;
    try{
      await api(`/api/projects/${btn.dataset.deleteProject}`,{method:'DELETE'});
      showToast('Project deleted');
      await renderOrgProjectsSettings();
    }catch(error){showToast(error.message,true)}
  });
}

async function renderOrgUsersSettings(){
  const tab=state.peopleTab||'members';
  const tabs=`<div class="filterbar">
    <button class="button ${tab==='members'?'primary':''}" id="peopleMembers">Members</button>
    <button class="button ${tab==='teams'?'primary':''}" id="peopleTeams">Teams</button>
    <button class="button ${tab==='invitations'?'primary':''}" id="peopleInvites">Invitations</button>
  </div>`;
  let body='';
  if(tab==='teams'){
    const [teams,roles]=await Promise.all([api('/api/teams'),api('/api/access/roles').catch(()=>[])]);
    const roleName=id=>(roles||[]).find(r=>r.id===id)?.name||'custom';
    body=`${tabs}<div class="card" style="margin-top:12px">${teams.map(t=>`<div class="module-card">
      <div><h3>${esc(t.name)}</h3><p>/${esc(t.slug)}${t.description?` · ${esc(t.description)}`:''} · ${t.roleId?`role: ${esc(roleName(t.roleId))}`:'no role'}</p></div>
      <button class="button" data-team="${esc(t.id)}">Open</button>
    </div>`).join('')||'<div class="empty">No teams yet.</div>'}</div>
    <p class="description" style="margin-top:12px"><button class="button text" data-route="/people">Open full People page</button></p>`;
  }else if(tab==='invitations'){
    const invitations=await api('/api/organisation/invitations');
    body=`${tabs}<div class="card" style="margin-top:12px">${invitations.map(i=>`<div class="module-card">
      <div><h3>${esc(i.email)}</h3><p>${esc(i.role)} · expires ${esc(new Date(i.expiresAt).toLocaleString())}${i.acceptedAt?' · accepted':''}</p></div>
      <span class="module-state">${i.acceptedAt?'Accepted':'Pending'}</span>
    </div>`).join('')||'<div class="empty">No invitations.</div>'}</div>`;
  }else{
    const members=await api('/api/organisation/members');
    body=`${tabs}<div class="card" style="margin-top:12px">${members.map(m=>`<div class="module-card">
      <span class="module-logo">${esc(initials(m.profile?.displayName||m.user?.username))}</span>
      <div><h3>${esc(m.profile?.displayName||m.user?.username)}</h3><p>@${esc(m.user?.username)} · ${esc(m.user?.email)} · ${esc(m.membership?.role)} · ${esc(m.membership?.status)}</p></div>
      <span class="module-state">${esc(m.membership?.role)}</span>
    </div>`).join('')||'<div class="empty">No members.</div>'}</div>
    <div class="modal-actions" style="margin-top:12px"><button class="button primary" id="inviteMember">Invite</button>
    <button class="button" data-route="/people">Open People</button></div>`;
  }
  renderSettingsShell('org','/organisation/settings/users','Users & Groups','Members, teams, and invitations for this organisation.',body);
  el('peopleMembers').onclick=()=>{state.peopleTab='members';renderOrgUsersSettings()};
  el('peopleTeams').onclick=()=>{state.peopleTab='teams';renderOrgUsersSettings()};
  el('peopleInvites').onclick=()=>{state.peopleTab='invitations';renderOrgUsersSettings()};
  const invite=el('inviteMember');
  if(invite)invite.onclick=()=>openModal(`<div class="modal-content"><h2>Invite member</h2>
    <label class="form-label">Email</label><input class="field" id="inviteEmail">
    <label class="form-label">Role</label><select class="field" id="inviteRole"><option>Member</option><option>Admin</option></select>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create invite</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      const created=await api('/api/organisation/invitations',{method:'POST',body:JSON.stringify({
        email:el('inviteEmail').value,role:el('inviteRole').value
      })});
      const link=`${location.origin}${location.pathname}${created.acceptPath}`;
      openModal(`<div class="modal-content"><h2>Invitation created</h2>
        <p class="description">Copy this link and share it. The token is shown once.</p>
        <input class="field" id="inviteLink" value="${esc(link)}" readonly>
        <div class="modal-actions"><button class="button primary" value="copy">Copy link</button></div></div>`,ev=>{
        if(ev.submitter?.value==='copy'){navigator.clipboard?.writeText(el('inviteLink').value);showToast('Link copied')}
      });
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-team]').forEach(button=>button.onclick=async()=>{
    const detail=await api(`/api/teams/${button.dataset.team}`);
    openModal(`<div class="modal-content"><h2>${esc(detail.team.name)}</h2>
      <p class="description">Members</p>
      ${(detail.members||[]).map(m=>`<div class="side-stat"><span>${esc(m.profile?.displayName||m.user?.username)}</span><strong>@${esc(m.user?.username)}</strong></div>`).join('')||'<p class="description">No members.</p>'}
      <label class="form-label">Add member user id</label><input class="field" id="teamAddUserId" placeholder="User GUID">
      <div class="modal-actions"><button class="button" value="cancel">Close</button><button class="button primary" value="add">Add member</button></div></div>`,async e=>{
      if(e.submitter?.value!=='add')return;
      try{
        await api(`/api/teams/${button.dataset.team}/members`,{method:'POST',body:JSON.stringify({userId:el('teamAddUserId').value})});
        showToast('Member added');
      }catch(error){showToast(error.message,true)}
    });
  });
}

async function renderOrgSecuritySettings(){
  renderSettingsShell('org','/organisation/settings/security','Authentication','Sign-in methods for this installation.',`
    <div class="card"><div class="card-header"><h2>Local accounts</h2><span class="pill">Enabled</span></div>
      <div class="card-body"><p class="description">Username and password authentication is enabled for this organisation.</p></div>
    </div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>Microsoft Entra ID</h2><span class="pill">Coming soon</span></div>
      <div class="card-body"><p class="description">Entra / OIDC federation is not configured in this tranche. External identity providers will appear here when available.</p></div>
    </div>`);
}

async function renderOrgPermissionsSettings(){
  const [roles,catalogue]=await Promise.all([
    api('/api/access/roles').catch(()=>[]),
    api('/api/access/permissions').catch(()=>[])
  ]);
  const byCategory={};
  (catalogue||[]).forEach(p=>(byCategory[p.category]??=[]).push(p));
  renderSettingsShell('org','/organisation/settings/permissions','Permissions','Groups and roles control fine-grained access inside projects.',`
    <div class="card"><div class="card-header"><h2>Roles</h2>
      <button class="button primary" id="createAccessRole">New custom role</button>
    </div>
    <div class="card-body" id="accessRolesList">
      ${(roles||[]).map(r=>`
        <div class="module-card" data-role-id="${esc(r.id)}" data-role-system="${r.isSystem?'1':'0'}">
          <span class="module-logo">${esc((r.name||'?')[0])}</span>
          <div>
            <h3>${esc(r.name)} ${r.isSystem?'<span class="pill">System</span>':''}</h3>
            <p class="description">${esc(r.description||r.slug)} · ${(r.permissions||[]).length} permissions</p>
          </div>
          <div class="settings-row-actions">
            ${r.isSystem?'':'<button class="button" data-edit-role="'+esc(r.id)+'">Edit</button>'}
            ${r.isSystem?'':'<button class="button danger" data-delete-role="'+esc(r.id)+'">Delete</button>'}
          </div>
        </div>`).join('')||'<div class="empty small">No roles yet.</div>'}
    </div></div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>Permission catalogue</h2></div>
      <div class="card-body">
        ${Object.entries(byCategory).map(([cat,items])=>`
          <p class="form-label">${esc(cat)}</p>
          <div class="perm-grid">${items.map(p=>`<div class="perm-chip" title="${esc(p.description)}"><code>${esc(p.key)}</code><span>${esc(p.title)}</span></div>`).join('')}</div>
        `).join('')||'<div class="empty small">Catalogue unavailable.</div>'}
      </div>
    </div>`);
  el('createAccessRole').onclick=()=>openAccessRoleEditor(null,catalogue);
  document.querySelectorAll('[data-edit-role]').forEach(btn=>btn.onclick=()=>{
    const role=(roles||[]).find(r=>r.id===btn.dataset.editRole);
    openAccessRoleEditor(role,catalogue);
  });
  document.querySelectorAll('[data-delete-role]').forEach(btn=>btn.onclick=async()=>{
    if(!confirm('Delete this custom role?'))return;
    try{
      await api(`/api/access/roles/${btn.dataset.deleteRole}`,{method:'DELETE'});
      showToast('Role deleted');
      await renderOrgPermissionsSettings();
    }catch(error){showToast(error.message,true)}
  });
}

function openAccessRoleEditor(role,catalogue){
  const selected=new Set(role?.permissions||[]);
  const groups={};
  (catalogue||[]).forEach(p=>(groups[p.category]??=[]).push(p));
  openModal(`<div class="modal-content"><h2>${role?'Edit role':'New custom role'}</h2>
    <label class="form-label">Name</label><input class="field" id="roleName" value="${esc(role?.name||'')}">
    <label class="form-label">Slug</label><input class="field" id="roleSlug" value="${esc(role?.slug||'')}" ${role?'disabled':''}>
    <label class="form-label">Description</label><input class="field" id="roleDescription" value="${esc(role?.description||'')}">
    <p class="form-label" style="margin-top:12px">Permissions</p>
    <div class="role-perm-editor">${Object.entries(groups).map(([cat,items])=>`
      <p class="description" style="margin:10px 0 6px">${esc(cat)}</p>
      ${items.map(p=>`<label class="choice-row"><input type="checkbox" data-role-perm="${esc(p.key)}" ${selected.has(p.key)?'checked':''}> ${esc(p.title)} <small>${esc(p.key)}</small></label>`).join('')}
    `).join('')}</div>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Save</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    const permissions=[...document.querySelectorAll('[data-role-perm]:checked')].map(i=>i.dataset.rolePerm);
    try{
      if(role){
        await api(`/api/access/roles/${role.id}`,{method:'PATCH',body:JSON.stringify({
          name:el('roleName').value,
          description:el('roleDescription').value,
          permissions
        })});
      }else{
        await api('/api/access/roles',{method:'POST',body:JSON.stringify({
          name:el('roleName').value,
          slug:el('roleSlug').value||null,
          description:el('roleDescription').value||null,
          permissions
        })});
      }
      showToast('Role saved');
      if(state.route==='/people'&&state.peopleTab==='roles')await renderPeople();
      else await renderOrgPermissionsSettings();
    }catch(error){showToast(error.message,true)}
  });
}

async function renderOrgBuildSettings(){
  let runners=[];
  try{runners=await api('/api/pipelines/runners')}catch{runners=[]}
  renderSettingsShell('org','/organisation/settings/build','Runners','Organisation-wide build agents. Register runners once; every project can use them.',`
    <div class="card"><div class="card-header"><h2>Runners</h2>
      <div class="header-actions"><button class="button" id="refreshRunners">Refresh</button><button class="button primary" id="addRunner">＋ Add Runner</button></div>
    </div>
    <div class="card-body" id="orgRunnersList">${(runners||[]).map(r=>`<article class="pipeline-row">
      <div><strong>${esc(r.name||r.id)}</strong><p class="description" style="margin:4px 0 0">${esc(r.operatingSystem||r.os||r.platform||'Unknown OS')} · ${esc((r.capabilities||[]).join(', ')||'no tags')} · v${esc(r.version||'?')}</p></div>
      <div class="header-actions"><span class="status ${statusClass(r.status)}">${esc(r.status||'—')}</span><button class="button danger" data-revoke-runner="${esc(r.id)}">Revoke</button></div>
    </article>`).join('')||'<div class="empty small">No runners registered yet. Add a runner to issue a registration token for this organisation.</div>'}
    </div></div>
    <div class="policy-box" style="margin-top:18px">Runners belong to the organisation, not a single project. Pipeline jobs from any project can schedule onto these agents.</div>`);
  el('refreshRunners').onclick=()=>renderOrgBuildSettings();
  el('addRunner').onclick=openAddRunner;
  document.querySelectorAll('[data-revoke-runner]').forEach(button=>button.onclick=async()=>{
    if(!confirm('Revoke this runner? It will need a new registration token.'))return;
    try{await api(`/api/pipelines/runners/${button.dataset.revokeRunner}`,{method:'DELETE'});showToast('Runner revoked');renderOrgBuildSettings()}catch(error){showToast(error.message,true)}
  });
}

async function renderProjectGeneralSettings(){
  const projectId=state.context?.project?.id;
  let project=(state.projects||[]).find(p=>p.id===projectId);
  if(!project){
    const projects=await api('/api/projects');
    state.projects=projects;
    project=projects.find(p=>p.id===projectId);
  }
  if(!project)return renderError(new Error('No project selected.'));

  let runs=[],members=[],audit=[];
  try{
    const jobs=[];
    if(hasModule('pipelines'))jobs.push(api('/api/pipelines/runs').then(r=>runs=r||[]).catch(()=>[]));
    jobs.push(api('/api/organisation/members').then(r=>members=r||[]).catch(()=>[]));
    jobs.push(api('/api/core/audit').then(r=>audit=r||[]).catch(()=>[]));
    await Promise.all(jobs);
  }catch{/* metrics degrade */}

  const open=openStatuses();
  const waiting=open.filter(change=>change.reviewers.some(review=>review.name===actor()&&review.status==='Requested'));
  const merged=state.changes.filter(change=>change.status==='Merged').slice().sort((a,b)=>new Date(b.mergedAt||b.updatedAt)-new Date(a.mergedAt||a.updatedAt)).slice(0,5);
  const terminal=runs.filter(r=>['Succeeded','Failed','Cancelled','PartiallySucceeded'].includes(r.status));
  const succeeded=terminal.filter(r=>r.status==='Succeeded'||r.status==='PartiallySucceeded').length;
  const successRate=terminal.length?Math.round((succeeded/terminal.length)*100):null;

  renderSettingsShell('project','/settings/general','Overview','Name, visibility, repository mode, and delivery metrics.',`
    <div class="card"><div class="card-header"><h2>Project profile</h2></div>
    <div class="card-body settings-form">
      <label class="form-label" for="projectName">Name</label>
      <input class="field" id="projectName" value="${esc(project.name||'')}">
      <label class="form-label" for="projectSlug">Slug</label>
      <input class="field" id="projectSlug" value="${esc(project.slug||'')}">
      <label class="form-label" for="projectDescription">Description</label>
      <textarea class="field" id="projectDescription" rows="3">${esc(project.description||'')}</textarea>
      <label class="form-label" for="projectVisibility">Visibility</label>
      <select class="field" id="projectVisibility">
        <option value="Private" ${project.visibility==='Private'?'selected':''}>Private</option>
        <option value="Organisation" ${project.visibility==='Organisation'?'selected':''}>Organisation</option>
      </select>
      <label class="form-label" for="projectRepoMode">Repository mode</label>
      <select class="field" id="projectRepoMode">
        <option value="SingleRepository" ${project.repositoryMode!=='MultiRepository'?'selected':''}>Single repository</option>
        <option value="MultiRepository" ${project.repositoryMode==='MultiRepository'?'selected':''}>Multi-repository</option>
      </select>
      <div class="modal-actions" style="margin-top:16px">
        <button class="button primary" id="projectSave">Save changes</button>
      </div>
    </div></div>
    <div class="card" style="margin-top:16px" id="projectDeliveryMetrics">
      <div class="card-header"><h2>Delivery metrics</h2></div>
      <div class="card-body">
        <div class="metric-row compact">
          <article class="metric-card"><p class="metric-label">Pipeline success</p><p class="metric-value">${successRate==null?'—':`${successRate}%`}</p><p class="metric-trend flat">${terminal.length?`${succeeded}/${terminal.length} terminal runs`:'No runs yet'}</p></article>
          <article class="metric-card"><p class="metric-label">Open PRs</p><p class="metric-value">${open.length}</p><p class="metric-trend flat">${waiting.length} waiting on you</p></article>
          <article class="metric-card"><p class="metric-label">Recently merged</p><p class="metric-value">${merged.length||'—'}</p><p class="metric-trend flat">Lead-time signal</p></article>
          <article class="metric-card"><p class="metric-label">Org team</p><p class="metric-value">${members.length||'—'}</p><p class="metric-trend flat">${audit.length} audit events</p></article>
        </div>
        <p class="description" style="margin-top:12px">Admin metrics live here so the project Overview stays a developer repo home.</p>
      </div>
    </div>`);
  el('projectSave').onclick=async()=>{
    try{
      const updated=await api(`/api/projects/${project.id}`,{method:'PATCH',body:JSON.stringify({
        name:el('projectName').value,
        slug:el('projectSlug').value,
        description:el('projectDescription').value,
        visibility:el('projectVisibility').value,
        repositoryMode:el('projectRepoMode').value
      })});
      if(state.context?.project){
        state.context.project.name=updated.name;
        state.context.project.key=updated.key||state.context.project.key;
      }
      state.projects=await api('/api/projects');
      el('projectLabel').textContent=updated.name;
      showToast('Project updated');
      await renderProjectGeneralSettings();
    }catch(error){showToast(error.message,true)}
  };
}

async function renderProjectModulesSettings(){
  const projectId=state.context?.project?.id;
  if(!projectId)return renderError(new Error('No project selected.'));
  let modules=[];
  try{modules=await api(`/api/projects/${projectId}/modules`)}catch{modules=[]}
  renderSettingsShell('project','/settings/modules','Modules','Enable organisation-installed modules for this project.',`
    <div class="card"><div class="card-header"><h2>Project modules</h2></div>
    <div class="card-body">
      <p class="description">Disabling a module hides it from this project only. Data is retained and the module remains available to other projects.</p>
      ${modules.map(m=>`
        <label class="choice-row module-toggle-row">
          <input type="checkbox" data-project-module-toggle="${esc(m.extensionId)}" ${m.enabled?'checked':''} ${m.organisationEnabled===false?'disabled':''}>
          <span><strong>${esc(m.name)}</strong><small>${m.organisationEnabled===false?'Disabled organisation-wide':'Installed at organisation level'}</small></span>
        </label>`).join('')||'<div class="empty small">No organisation modules are installed yet. Install modules from Organisation Settings.</div>'}
      <div class="modal-actions" style="margin-top:16px">
        <button class="button primary" id="projectModulesSave" ${modules.length?'':'disabled'}>Save</button>
      </div>
    </div></div>`);
  el('projectModulesSave').onclick=async()=>{
    try{
      const enabledExtensionIds=[...document.querySelectorAll('[data-project-module-toggle]:checked')].map(i=>i.dataset.projectModuleToggle);
      await api(`/api/projects/${projectId}/modules`,{method:'PUT',body:JSON.stringify({enabledExtensionIds})});
      await reloadComposition();
      showToast('Project modules updated');
      await renderProjectModulesSettings();
    }catch(error){showToast(error.message,true)}
  };
}

async function renderProjectMembersSettings(){
  const projectId=state.context?.project?.id;
  if(!projectId)return renderError(new Error('No project selected.'));
  const accessTab=state.projectAccessTab||'teams';
  const [members,teams,orgMembers,allTeams,roles]=await Promise.all([
    api(`/api/projects/${projectId}/members`).catch(()=>[]),
    api(`/api/projects/${projectId}/teams`).catch(()=>[]),
    api('/api/organisation/members').catch(()=>[]),
    api('/api/teams').catch(()=>[]),
    api('/api/access/roles').catch(()=>[])
  ]);
  const projectRoles=(roles||[]).filter(r=>!r.scopeType||r.scopeType==='Project'||['reader','viewer','developer','reviewer','builder','deployer','project-admin','deploy-operator'].includes(r.slug));
  const roleOptions=`<option value="">Select a project role</option>${projectRoles.map(r=>`<option value="${esc(r.id)}">${esc(r.name)}</option>`).join('')}`;
  const roleLabel=grant=>grant.roleName?esc(grant.roleName):'Access only';
  const canManage=canAny('projects.manage','project.members.manage');
  const tabs=`<div class="filterbar">
    <button class="button ${accessTab==='teams'?'primary':''}" id="accessTeamsTab">Teams</button>
    <button class="button ${accessTab==='members'?'primary':''}" id="accessMembersTab">Individual members</button>
  </div>`;
  renderSettingsShell('project','/settings/members','Members & Teams','Grant project access to teams and individual members. Roles combine additively.',`
    ${tabs}
    ${accessTab==='teams'?`<div class="card"><div class="card-header"><h2>Teams</h2>
      ${canManage?'<button class="button primary" id="addProjectTeam">Add team</button>':''}
    </div>
    <div class="card-body table-wrap"><table class="data-table"><thead><tr><th>Team</th><th>Relationship</th><th>Project role</th><th></th></tr></thead>
      <tbody>${(teams||[]).map(t=>`<tr>
        <td><strong>${esc(t.name||t.teamId)}</strong><div class="muted">/${esc(t.slug||'—')}</div></td>
        <td>${esc(t.relationship||(t.owning?'Owner':'Access'))}</td>
        <td>${roleLabel(t)}</td>
        <td>${canManage?`<button class="button danger text" data-revoke-team="${esc(t.teamId)}">Remove</button>`:''}</td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty small">No team grants.</div></td></tr>'}</tbody></table></div></div>`
    :`<div class="card"><div class="card-header"><h2>Individual members</h2>
      ${canManage?'<button class="button primary" id="addProjectMember">Add member</button>':''}
    </div>
    <div class="card-body table-wrap"><table class="data-table"><thead><tr><th>Member</th><th>Project role</th><th>Access type</th><th></th></tr></thead>
      <tbody>${(members||[]).map(m=>`<tr>
        <td><strong>${esc(m.displayName||m.username||m.userId)}</strong><div class="muted">@${esc(m.username||'—')}</div></td>
        <td>${roleLabel(m)}</td>
        <td>Direct</td>
        <td>${canManage?`<button class="button danger text" data-revoke-member="${esc(m.userId)}">Remove</button>`:''}</td>
      </tr>`).join('')||'<tr><td colspan="4"><div class="empty small">No direct member grants.</div></td></tr>'}</tbody></table></div></div>`}`);
  el('accessTeamsTab').onclick=()=>{state.projectAccessTab='teams';renderProjectMembersSettings()};
  el('accessMembersTab').onclick=()=>{state.projectAccessTab='members';renderProjectMembersSettings()};
  const addMember=el('addProjectMember');
  if(addMember)addMember.onclick=()=>{
    const options=(orgMembers||[]).map(m=>`<option value="${esc(m.user?.id||m.userId)}">${esc(m.profile?.displayName||m.user?.username)}</option>`).join('');
    openModal(`<div class="modal-content"><h2>Add project access</h2>
      <label class="form-label">Member</label><select class="field" id="projectMemberId">${options||'<option value="">No members</option>'}</select>
      <label class="form-label">Project role</label><select class="field" id="projectMemberRole">${roleOptions}</select>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Grant access</button></div></div>`,async e=>{
      if(e.submitter?.value!=='submit')return;
      try{
        await api(`/api/projects/${projectId}/members`,{method:'POST',body:JSON.stringify({
          userId:el('projectMemberId').value,
          roleId:el('projectMemberRole').value||null
        })});
        showToast('Member added');await renderProjectMembersSettings();
      }catch(error){showToast(error.message,true)}
    });
  };
  const addTeam=el('addProjectTeam');
  if(addTeam)addTeam.onclick=()=>{
    const options=(allTeams||[]).map(t=>`<option value="${esc(t.id)}">${esc(t.name)}</option>`).join('');
    openModal(`<div class="modal-content"><h2>Add project access</h2>
      <label class="form-label">Team</label><select class="field" id="projectTeamId">${options||'<option value="">No teams</option>'}</select>
      <label class="form-label">Project role</label><select class="field" id="projectTeamRole">${roleOptions}</select>
      <p class="description">All active team members inherit this project role.</p>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Grant access</button></div></div>`,async e=>{
      if(e.submitter?.value!=='submit')return;
      try{
        await api(`/api/projects/${projectId}/teams`,{method:'POST',body:JSON.stringify({
          teamId:el('projectTeamId').value,
          roleId:el('projectTeamRole').value||null
        })});
        showToast('Team added');await renderProjectMembersSettings();
      }catch(error){showToast(error.message,true)}
    });
  };
  document.querySelectorAll('[data-revoke-member]').forEach(btn=>btn.onclick=async()=>{
    try{
      await api(`/api/projects/${projectId}/members/${btn.dataset.revokeMember}`,{method:'DELETE'});
      showToast('Member removed');await renderProjectMembersSettings();
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-revoke-team]').forEach(btn=>btn.onclick=async()=>{
    try{
      await api(`/api/projects/${projectId}/teams/${btn.dataset.revokeTeam}`,{method:'DELETE'});
      showToast('Team removed');await renderProjectMembersSettings();
    }catch(error){showToast(error.message,true)}
  });
}

async function renderProjectRolesSettings(){
  const roles=await api('/api/access/roles').catch(()=>[]);
  const projectRoles=(roles||[]).filter(r=>r.scopeType==='Project'||['reader','viewer','developer','reviewer','builder','deployer','project-admin','deploy-operator'].includes(r.slug));
  renderSettingsShell('project','/settings/roles','Roles','Project roles and their permissions. Module permissions appear when the module is enabled.',`
    <div class="card">${projectRoles.map(role=>`<div class="module-card">
      <div><h3>${esc(role.name)}</h3><p>${role.isSystem?'System':'Custom'} · ${role.permissions?.length||0} permissions</p>
        <p class="description">${(role.permissions||[]).slice(0,8).map(p=>esc(p)).join(', ')}${(role.permissions||[]).length>8?'…':''}</p>
      </div>
    </div>`).join('')||'<div class="empty">No project roles.</div>'}</div>`);
}

async function renderProjectPermissionsSettings(){
  const projectId=state.context?.project?.id;
  if(!projectId)return renderError(new Error('No project selected.'));
  const [effective,grants,members]=await Promise.all([
    api(`/api/access/effective?scopeType=Project&scopeId=${projectId}`).catch(()=>({permissions:[],sources:{}})),
    api(`/api/access/grants?scopeType=Project&scopeId=${projectId}`).catch(()=>[]),
    api('/api/organisation/members').catch(()=>[])
  ]);
  const canManage=canAny('project.permissions.manage','projects.manage','permissions.manage');
  renderSettingsShell('project','/settings/permissions','Permissions','Direct grants and an access inspector for this project.',`
    <div class="card"><div class="card-header"><h2>Direct user grants</h2>
      ${canManage?'<button class="button primary" id="grantProjectPerm">Grant direct permission</button>':''}
    </div>
    <div class="card-body table-wrap"><table class="data-table"><thead><tr><th>User</th><th>Permission</th><th>Source</th></tr></thead>
      <tbody>${(grants||[]).map(g=>{
        const member=(members||[]).find(m=>(m.user?.id||m.userId)===g.userId);
        return `<tr><td>${esc(member?.profile?.displayName||member?.user?.username||g.userId)}</td><td><code>${esc(g.permissionId)}</code></td><td>Direct</td></tr>`;
      }).join('')||'<tr><td colspan="3"><div class="empty small">No direct grants.</div></td></tr>'}</tbody></table></div></div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>Access inspector</h2>
      <button class="button" id="inspectAccess">Inspect current user</button>
    </div>
    <div class="card-body">
      <p class="description">Effective permissions for you on this project: <strong>${(effective.permissions||[]).length}</strong></p>
      <div class="table-wrap"><table class="data-table"><thead><tr><th>Permission</th><th></th></tr></thead>
        <tbody>${(effective.permissions||[]).slice(0,40).map(p=>`<tr><td><code>${esc(p)}</code></td><td><button class="button text" data-explain="${esc(p)}">Why?</button></td></tr>`).join('')
          ||'<tr><td colspan="2"><div class="empty small">No effective permissions.</div></td></tr>'}</tbody></table></div>
    </div></div>`);
  document.querySelectorAll('[data-explain]').forEach(btn=>btn.onclick=()=>openPermissionExplain(btn.dataset.explain,'Project',projectId));
  const inspect=el('inspectAccess');
  if(inspect)inspect.onclick=()=>openPermissionExplain((effective.permissions||[])[0]||'project.read','Project',projectId);
  const grant=el('grantProjectPerm');
  if(grant)grant.onclick=async()=>{
    const catalogue=await api('/api/access/permissions').catch(()=>[]);
    const projectPerms=(catalogue||[]).filter(p=>(p.allowedScopes||[]).includes('Project')||String(p.key).startsWith('review.')||String(p.key).startsWith('deploy.')||String(p.key).startsWith('pipelines.')||String(p.key).startsWith('git.')||p.key==='project.read');
    openModal(`<div class="modal-content"><h2>Grant direct permission</h2>
      <label class="form-label">Member</label><select class="field" id="grantUserId">${(members||[]).map(m=>`<option value="${esc(m.user?.id)}">${esc(m.profile?.displayName||m.user?.username)}</option>`).join('')}</select>
      <label class="form-label">Permission</label><select class="field" id="grantPermissionId">${projectPerms.map(p=>`<option value="${esc(p.key)}">${esc(p.key)}</option>`).join('')}</select>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Grant</button></div></div>`,async e=>{
      if(e.submitter?.value!=='submit')return;
      try{
        await api('/api/access/grants',{method:'POST',body:JSON.stringify({
          userId:el('grantUserId').value,
          permissionId:el('grantPermissionId').value,
          scopeType:'Project',
          scopeId:projectId
        })});
        showToast('Permission granted');renderProjectPermissionsSettings();
      }catch(error){showToast(error.message,true)}
    });
  };
}

async function renderProjectRepositoriesSettings(){
  const integration=await api('/api/core/integrations/github');
  const local=state.local;
  renderSettingsShell('project','/settings/repositories','Repositories','Local working copy and GitHub source providers.',`
  <div class="panel-grid"><div>
    <div class="card"><div class="card-header"><h2>Local repository</h2>
      ${local?.associated?`<div style="display:flex;gap:8px"><button class="button" id="settingsOpenFolder">Open folder</button><button class="button danger" id="disconnectLocal">Disconnect</button></div>`:`<button class="button primary" id="connectLocal">Connect local repository</button>`}
    </div>
    ${local?.associated?`<div class="card-body">
      <div class="side-stat"><span>Path</span><strong>${esc(local.status?.root||local.association?.root||local.association?.path)}</strong></div>
      <div class="side-stat"><span>Branch</span><strong>${esc(local.status?.currentBranch||'—')}</strong></div>
      <div class="side-stat"><span>Remote</span><strong>${esc(local.status?.originUrl||'—')}</strong></div>
      <div class="side-stat"><span>Detected GitHub</span><strong>${esc(local.status?.detectedGitHub?.fullName||'—')}</strong></div>
    </div>`:'<div class="empty small">Associate a local working copy to surface branch and dirty-tree status on the overview.</div>'}
    </div>
    <div class="card" style="margin-top:18px"><div class="card-header"><h2>Source repositories</h2><button class="button primary" id="connectRepository">Connect GitHub</button></div>
      ${state.connections.map(connection=>`<div class="module-card"><span class="module-logo">G</span><div><h3>${esc(connection.repositoryId.owner)}/${esc(connection.repositoryId.name)}</h3><p>${esc(connection.url)} · default ${esc(connection.defaultBranch)}</p></div><span class="module-state">● Connected</span></div>`).join('')||'<div class="empty">No repository connected.</div>'}
    </div>
  </div>
  <aside><div class="card"><div class="card-header"><h2>GitHub credential</h2></div><div class="card-body">
    <p class="description">${integration.configured?'A PAT is encrypted at rest. Its value is never returned by the API.':'Public repositories work without a token. Add a PAT for private repositories and merge operations.'}</p>
    <button class="button ${integration.configured?'':'primary'}" id="credentialButton">${integration.configured?'Replace token':'Add token'}</button>
    ${integration.configured?'<button class="button danger" id="deleteCredential">Delete</button>':''}
  </div></div></aside></div>`);
  const connectLocal=el('connectLocal');if(connectLocal)connectLocal.onclick=openConnectLocal;
  const disconnectLocal=el('disconnectLocal');if(disconnectLocal)disconnectLocal.onclick=disconnectLocalRepository;
  const settingsOpenFolder=el('settingsOpenFolder');if(settingsOpenFolder)settingsOpenFolder.onclick=openLocalFolder;
  el('connectRepository').onclick=openConnect;
  el('credentialButton').onclick=openCredential;
  if(el('deleteCredential'))el('deleteCredential').onclick=deleteCredential;
}

async function renderProjectReviewSettings(){
  if(!hasModule('review'))return renderError(new Error('Review module is not enabled.'));
  const policy=await api('/api/review/policy');
  const multi=!!policy.multiApprovalLicensed;
  renderSettingsShell('project','/settings/review','Policies','Pull request approval requirements for this project.',`
    <div class="card"><div class="card-header"><h2>Approval policy</h2>
      ${multi?'':'<span class="pill">Community</span>'}
    </div>
    <div class="card-body settings-form">
      <label class="form-label" for="minimumApprovals">Minimum approvals</label>
      <input class="field" id="minimumApprovals" type="number" min="1" value="${esc(String(policy.minimumApprovals??1))}" ${multi?'':'disabled'}>
      <p class="description">${multi
        ?`Active policy: ${esc(policy.activePolicy||'Multi-approval')}.`
        :'Multi-approval controls require an Enterprise Review licence. Single approval is active.'}</p>
      <div class="modal-actions" style="margin-top:16px">
        <button class="button primary" id="saveReviewPolicy" ${multi?'':'disabled'}>Save policy</button>
      </div>
    </div></div>`);
  const save=el('saveReviewPolicy');
  if(save&&!save.disabled)save.onclick=async()=>{
    try{
      await api('/api/review/policy',{method:'PUT',body:JSON.stringify({minimumApprovals:Number(el('minimumApprovals').value)||1})});
      showToast('Review policy saved');
      await renderProjectReviewSettings();
    }catch(error){showToast(error.message,true)}
  };
}

async function renderProjectBuildSettings(){
  return navigate('/organisation/settings/build');
}

async function renderSettings(){
  return renderProjectSettings('/settings/repositories');
}

async function renderAudit(){
  const audit=await api('/api/core/audit');
  renderSettingsShell('org','/organisation/settings/audit','Audit log','Security and workflow actions, separate from Change activity.',`
    <div class="card">${audit.map(item=>`<div class="audit-row"><span class="event-icon">◎</span><div><strong>${esc(item.actor)}</strong> <code>${esc(item.action)}</code><br><small>${esc(item.module)} · ${esc(item.resource)}</small></div><small>${new Date(item.timestamp).toLocaleString()}</small></div>`).join('')||'<div class="empty">No audited actions yet.</div>'}</div>`);
}

async function renderModules(){
  const catalogue=await api('/api/platform/extensions/modules');
  const moduleOrder={code:1,git:2,review:3,pipelines:4,build:4,deploy:5};
  const isTierClone=id=>/\.(team|enterprise|commercial)$/i.test(id||'');
  const rank=item=>{
    const id=(item.runtimeId||item.extensionId||'').toLowerCase();
    for(const [key,value] of Object.entries(moduleOrder)){
      if(id===key||id.endsWith('.'+key)||id.includes('.'+key+'.'))return value;
    }
    return 50;
  };
  const installed=catalogue.filter(x=>x.installed).sort((a,b)=>rank(a)-rank(b)||String(a.name).localeCompare(b.name));
  const available=catalogue.filter(x=>!x.installed && !isTierClone(x.extensionId))
    .sort((a,b)=>rank(a)-rank(b)||String(a.name).localeCompare(b.name));
  const card=item=>{
    const edition=editionLabel(item.edition);
    return `
    <div class="module-card" data-extension-id="${esc(item.extensionId)}" data-module-id="${esc(item.runtimeId||'')}" data-extension-state="${esc(item.state)}" data-edition="${esc(item.edition||'Community')}">
      <span class="module-logo">${esc((item.name||'?')[0])}</span>
      <div>
        <h3>${esc(item.name)} <span class="pill module-edition">${esc(edition)}</span></h3>
        <p>${esc(item.summary)}</p>
        <p class="description">${(item.highlights||[]).map(esc).join(' · ')}</p>
        <p class="description" data-capabilities>${(item.capabilities||[]).map(esc).join(' · ')||(item.installed?(item.enabled?'Enabled':'Installed · Disabled'):(item.bundled?'Available bundled package':'Not bundled'))}</p>
      </div>
      <div class="settings-row-actions">
        ${!item.installed&&item.bundled?`<button class="button primary" data-ext-install="${esc(item.extensionId)}">Install</button>`:''}
        ${item.installed&&!item.enabled?`<button class="button primary" data-ext-enable="${esc(item.extensionId)}">Enable</button>`:''}
        ${item.enabled?`<button class="button" data-ext-disable="${esc(item.extensionId)}">Disable</button>`:''}
        ${item.installed&&!item.enabled?`<button class="button danger" data-ext-uninstall="${esc(item.extensionId)}">Uninstall</button>`:''}
        ${item.restartRequired?'<span class="pill">Restart required</span>':''}
      </div>
    </div>`;
  };
  renderSettingsShell('org','/organisation/settings/modules','Modules','Edition is licence-driven; Team/Enterprise unlock capabilities on the same module.',`
    <p class="description" style="margin:0 0 14px">One card per capability. Team and Enterprise are upgrades on the installed module, not separate catalogue rows.</p>
    <div class="card"><div class="card-header"><h2>Installed</h2></div>
      <div class="card-body" id="modulesList">${installed.map(card).join('')||'<div class="empty small">No modules installed yet.</div>'}</div>
    </div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>Available</h2></div>
      <div class="card-body">${available.map(card).join('')||'<div class="empty small">No additional modules available.</div>'}</div>
    </div>`);
  wireExtensionActions(renderModules);
}

async function renderConnectors(){
  const catalogue=await api('/api/platform/extensions/connectors');
  const installed=catalogue.filter(x=>x.installed);
  const available=catalogue.filter(x=>!x.installed);
  const card=item=>`
    <div class="module-card" data-extension-id="${esc(item.extensionId)}">
      <span class="module-logo">${esc((item.name||'?')[0])}</span>
      <div>
        <h3>${esc(item.name)}</h3>
        <p>${esc(item.summary)}</p>
        <p class="description">${item.installed?(item.enabled?'Installed · Enabled':'Installed · Disabled'):(item.bundled?'Available':'Coming soon')}</p>
      </div>
      <div class="settings-row-actions">
        ${!item.installed&&item.bundled?`<button class="button primary" data-ext-install="${esc(item.extensionId)}">Install</button>`:''}
        ${item.installed&&!item.enabled?`<button class="button primary" data-ext-enable="${esc(item.extensionId)}">Enable</button>`:''}
        ${item.enabled?`<button class="button" data-ext-disable="${esc(item.extensionId)}">Disable</button>`:''}
        ${item.installed&&!item.enabled?`<button class="button danger" data-ext-uninstall="${esc(item.extensionId)}">Uninstall</button>`:''}
      </div>
    </div>`;
  renderSettingsShell('org','/organisation/settings/connectors','Connectors','Connect external systems. Connectors do not add top-level Project navigation.',`
    <div class="card"><div class="card-header"><h2>Installed</h2></div>
      <div class="card-body">${installed.map(card).join('')||'<div class="empty small">No connectors installed.</div>'}</div>
    </div>
    <div class="card" style="margin-top:16px"><div class="card-header"><h2>Available</h2></div>
      <div class="card-body">${available.map(card).join('')||'<div class="empty small">No additional connectors listed.</div>'}</div>
    </div>`);
  wireExtensionActions(renderConnectors);
}

function wireExtensionActions(rerender){
  document.querySelectorAll('[data-ext-install]').forEach(btn=>btn.onclick=async()=>{
    const id=btn.dataset.extInstall;
    openModal(`<div class="modal-content"><h2>Install extension?</h2>
      <p class="description"><code>${esc(id)}</code> will be installed and enabled. Database migrations may run.</p>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Install</button></div></div>`,async e=>{
      if(e.submitter?.value!=='submit')return;
      try{
        await api(`/api/platform/extensions/${encodeURIComponent(id)}/install`,{method:'POST',body:JSON.stringify({enable:true})});
        showToast('Extension installed');
        await reloadComposition();
        await rerender();
      }catch(error){showToast(error.message,true)}
    });
  });
  document.querySelectorAll('[data-ext-enable]').forEach(btn=>btn.onclick=async()=>{
    try{
      await api(`/api/platform/extensions/${encodeURIComponent(btn.dataset.extEnable)}/enable`,{method:'POST',body:'{}'});
      showToast('Extension enabled');await reloadComposition();await rerender();
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-ext-disable]').forEach(btn=>btn.onclick=async()=>{
    if(!confirm('Disable this extension? Data is retained.'))return;
    try{
      await api(`/api/platform/extensions/${encodeURIComponent(btn.dataset.extDisable)}/disable`,{method:'POST',body:'{}'});
      showToast('Extension disabled');await reloadComposition();await rerender();
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-ext-uninstall]').forEach(btn=>btn.onclick=async()=>{
    if(!confirm('Uninstall this extension? Data is preserved by default.'))return;
    try{
      await api(`/api/platform/extensions/${encodeURIComponent(btn.dataset.extUninstall)}/uninstall`,{method:'POST',body:JSON.stringify({preserveData:true})});
      showToast('Extension uninstalled');await reloadComposition();await rerender();
    }catch(error){showToast(error.message,true)}
  });
}

async function renderLicensing(){
  const licence=await api('/api/licensing');
  const modules=(licence.modules||[]).map(m=>`
    <tr data-licence-module="${esc(m.moduleId||m.name)}" data-edition="${esc(m.edition)}"><td>${esc(m.name)}</td><td><span class="pill">${esc(editionLabel(m.edition))}</span></td><td>${m.installed?'Installed':'Not installed'}</td></tr>`).join('');
  renderSettingsShell('org','/organisation/settings/license','License','Licensing is capability and module based for this installation.',`
  <div class="card" id="licensingStatus">
    <div class="card-header"><h2>Current licence</h2><span class="pill" id="licensingStatusPill">${esc(licence.status)}</span></div>
    <div class="card-body setup-summary">
      <div><span>Type</span><strong id="licensingMode">${esc(licenceModeLabel(licence.mode))}</strong></div>
      <div><span>Customer</span><strong>${esc(licence.customerId||'—')}</strong></div>
      <div><span>Expires</span><strong>${licence.expiresAt?esc(new Date(licence.expiresAt).toLocaleDateString()):'—'}</strong></div>
      <div><span>Capabilities</span><strong id="licensingCapabilityCount">${licence.capabilityCount??0} enabled</strong></div>
      <div><span>Instance</span><strong class="mono">${esc(licence.instanceId||'—')}</strong></div>
    </div>
  </div>
  <div class="card" style="margin-top:16px">
    <div class="card-header"><h2>Modules</h2></div>
    <div class="card-body"><table class="data-table" id="licensingModules"><thead><tr><th>Module</th><th>Licence</th><th>Installed</th></tr></thead><tbody>${modules||'<tr><td colspan="3">No modules</td></tr>'}</tbody></table></div>
  </div>
  <div class="card" style="margin-top:16px" id="licenceUploadCard">
    <div class="card-header"><h2>${licence.mode==='Commercial'?'Replace Enterprise licence':'Upload Enterprise licence'}</h2></div>
    <div class="card-body">
      <div class="licence-dropzone" id="licenceDropzone">
        <input type="file" id="licensingFile" accept=".json,application/json" hidden>
        <input type="hidden" id="licensingPayload" value="">
        <div class="licence-dropzone-inner">
          <span class="licence-dropzone-icon" aria-hidden="true">⇪</span>
          <strong id="licensingFileName">Drop a signed licence file here</strong>
          <p class="description">or <button type="button" class="button text" id="licensingBrowse">browse</button> for a <code>.json</code> file from your vendor</p>
        </div>
      </div>
      <div class="modal-actions" style="margin-top:16px">
        <button class="button" id="licensingCommunity">Use Community</button>
        ${licence.mode==='Commercial'?`<button class="button" id="licensingRemove">Remove Enterprise</button>`:''}
        <button class="button primary" id="licensingInstall">Validate &amp; install</button>
      </div>
    </div>
  </div>`);
  wireLicenceFileInput('licensingFile','licensingPayload','licensingFileName');
  wireLicenceDropzone();
  el('licensingBrowse').onclick=()=>el('licensingFile')?.click();
  el('licensingCommunity').onclick=async()=>{
    try{await api('/api/licensing/community',{method:'POST',body:'{}'});showToast('Community licence active');await renderLicensing()}
    catch(error){showToast(error.message,true)}
  };
  el('licensingInstall').onclick=async()=>{
    try{
      const payload=el('licensingPayload').value;
      if(!payload.trim())return showToast('Choose a licence file first',true);
      await api('/api/licensing/commercial',{method:'POST',body:JSON.stringify({payload})});
      showToast('Enterprise licence installed');
      await renderLicensing();
    }
    catch(error){showToast(error.message,true)}
  };
  const remove=el('licensingRemove');
  if(remove)remove.onclick=async()=>{
    if(!confirm('Remove the Enterprise licence and continue with Community?'))return;
    try{await api('/api/licensing/commercial',{method:'DELETE'});showToast('Enterprise licence removed');await renderLicensing()}
    catch(error){showToast(error.message,true)}
  };
}

function wireLicenceDropzone(){
  const zone=el('licenceDropzone');
  const input=el('licensingFile');
  if(!zone||!input||zone.dataset.bound)return;
  zone.dataset.bound='1';
  zone.addEventListener('dragover',e=>{e.preventDefault();zone.classList.add('dragover')});
  zone.addEventListener('dragleave',()=>zone.classList.remove('dragover'));
  zone.addEventListener('drop',e=>{
    e.preventDefault();
    zone.classList.remove('dragover');
    const file=e.dataTransfer?.files?.[0];
    if(!file)return;
    const transfer=new DataTransfer();
    transfer.items.add(file);
    input.files=transfer.files;
    input.dispatchEvent(new Event('change',{bubbles:true}));
  });
  zone.addEventListener('click',e=>{
    if(e.target.closest('#licensingBrowse'))return;
    input.click();
  });
}

function refreshSettingsAfterSourceChange(){
  if(state.route?.startsWith('/settings'))return renderProjectSettings(state.route);
  if(state.route==='/overview')return renderOverview();
  return renderProjectRepositoriesSettings();
}

function shortId(id){return String(id||'').replace(/-/g,'').slice(0,8)}
function durationLabel(started,completed){
  if(!started)return '—';
  const end=completed?new Date(completed):new Date();
  const seconds=Math.max(0,(end-new Date(started))/1000);
  if(seconds<60)return `${seconds.toFixed(1)}s`;
  return `${Math.floor(seconds/60)}m ${Math.round(seconds%60)}s`;
}
function triggerLabels(triggers){return (triggers||[]).map(t=>typeof t==='number'?['Manual','Push','ChangeOpened','ChangeUpdated'][t]||t:t).join(', ')}

async function renderPipelines(route){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  const project=state.context?.project;
  const [definitions,runs]=await Promise.all([
    api('/api/pipelines/definitions'),
    api('/api/pipelines/runs').catch(()=>[])
  ]);
  const id=route.split('/')[2];
  const selected=id?definitions.find(d=>d.id===id):null;
  if(!state.pipelinesFilter)state.pipelinesFilter='all';
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Build <span>/</span> Pipelines${selected?` <span>/</span> ${esc(selected.name)}`:''}`);

  const latestByDef={};
  (runs||[]).forEach(run=>{
    const key=String(run.definitionId||'');
    if(!key)return;
    const prev=latestByDef[key];
    if(!prev||new Date(run.startedAt||run.createdAt||0)>new Date(prev.startedAt||prev.createdAt||0))
      latestByDef[key]=run;
  });

  const terminal=(runs||[]).filter(r=>['Succeeded','Failed','Cancelled','PartiallySucceeded'].includes(r.status));
  const succeeded=terminal.filter(r=>r.status==='Succeeded'||r.status==='PartiallySucceeded').length;
  const successRate=terminal.length?Math.round((succeeded/terminal.length)*100):null;
  const activeRuns=(runs||[]).filter(r=>isActiveStatus(r.status)).length;
  const avgSeconds=(()=>{
    const samples=terminal.map(r=>{
      if(!r.startedAt||!r.completedAt)return null;
      return Math.max(0,(new Date(r.completedAt)-new Date(r.startedAt))/1000);
    }).filter(v=>v!=null);
    if(!samples.length)return null;
    return samples.reduce((a,b)=>a+b,0)/samples.length;
  })();
  const avgLabel=avgSeconds==null?'—':avgSeconds<60?`${avgSeconds.toFixed(0)}s`:`${Math.floor(avgSeconds/60)}m ${Math.round(avgSeconds%60)}s`;

  const enriched=definitions.map(def=>{
    const latest=latestByDef[String(def.id)]||null;
    return {def,latest};
  });

  const statusBucket=item=>{
    const s=item.latest?.status;
    if(!s)return 'scheduled';
    if(isActiveStatus(s))return 'running';
    if(s==='Succeeded'||s==='PartiallySucceeded')return 'passed';
    if(s==='Failed'||s==='Cancelled')return 'failed';
    return 'scheduled';
  };
  const counts={
    all:enriched.length,
    running:enriched.filter(i=>statusBucket(i)==='running').length,
    passed:enriched.filter(i=>statusBucket(i)==='passed').length,
    failed:enriched.filter(i=>statusBucket(i)==='failed').length,
    scheduled:enriched.filter(i=>statusBucket(i)==='scheduled').length
  };

  const filter=state.pipelinesFilter;
  const search=(state.pipelinesSearch||'').toLowerCase();
  const filtered=enriched.filter(item=>{
    if(filter!=='all'&&statusBucket(item)!==filter)return false;
    if(search){
      const hay=`${item.def.name} ${triggerLabels(item.def.triggers)} ${item.latest?.ref||''}`.toLowerCase();
      if(!hay.includes(search))return false;
    }
    return true;
  });

  const activity=(runs||[]).slice(0,5).map(run=>({
    actor:run.requestedBy||run.trigger||'Pipeline',
    detail:`${String(run.status).toLowerCase()} ${run.definitionName||'pipeline'} #${shortId(run.id)}`,
    when:relativeTime(run.completedAt||run.startedAt||run.createdAt),
    initials:initials(run.requestedBy||run.definitionName||'P')
  }));

  const templates=[
    {name:'.NET Application',desc:'Restore, build, test, and publish checks',icon:'.N'},
    {name:'Node.js API',desc:'Install, lint, test, and package',icon:'N'},
    {name:'Docker Build',desc:'Build and push a container image',icon:'D'},
    {name:'Infrastructure',desc:'Plan and apply infrastructure changes',icon:'I'}
  ];

  el('content').innerHTML=`
  <section class="project-hero pipe-hero">
    <div class="project-hero-top">
      <div class="project-identity">
        <div class="project-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><path d="M4 17l4-10h8l4 10H4z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="M9 17v3M15 17v3M8 10h8" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </div>
        <div>
          <h1>Build <span class="pill visibility-pill">Public</span></h1>
          <p class="project-desc">Monitor, run, and review CI/CD workflows for this project.</p>
        </div>
      </div>
      <div class="header-actions">
        <button class="button" data-route="/files">&lt;&gt; Code</button>
        <button class="button" id="runPipelineQuick" ${definitions[0]?.enabled===false?'disabled':''}>Run pipeline</button>
        <button class="button" id="refreshPipelines">Refresh</button>
        <button class="button primary" id="newPipelineHint">＋ New pipeline</button>
      </div>
    </div>
  </section>

  <div class="pipe-metric-row">
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Pipeline success rate</p>
          <p class="metric-value">${successRate==null?'—':`${successRate}%`}</p>
          <p class="metric-trend ${successRate==null?'flat':successRate>=80?'up':'down'}">${terminal.length?`${succeeded}/${terminal.length} completed runs`:'No completed runs'}</p>
        </div>
        <div class="metric-ring" style="--pct:${successRate??0}"></div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Average duration</p>
          <p class="metric-value">${esc(avgLabel)}</p>
          <p class="metric-trend flat">Across recent terminal runs</p>
        </div>
        <div class="metric-icon">◷</div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Active runs</p>
          <p class="metric-value">${activeRuns}</p>
          <p class="metric-trend ${activeRuns?'up':'flat'}">${(runs||[]).length} total runs</p>
        </div>
        <div class="metric-icon green">≋</div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Definitions</p>
          <p class="metric-value">${definitions.length}</p>
          <p class="metric-trend flat">${definitions.filter(d=>d.enabled).length} enabled</p>
        </div>
        <div class="metric-icon amber">◎</div>
      </div>
    </article>
  </div>

  <div class="pr-status-tabs pipe-status-tabs" role="tablist">
    ${[['all','All'],['running','Running'],['passed','Passed'],['failed','Failed'],['scheduled','Scheduled']].map(([key,label])=>`
      <button type="button" class="pr-status-tab ${filter===key?'active':''}" data-pipe-filter="${key}">${label} <span class="count">${counts[key]}</span></button>`).join('')}
  </div>

  <div class="pr-filterbar filterbar pipe-filterbar">
    <input class="field compact pr-search" id="pipelineSearch" placeholder="Search pipelines…" value="${esc(state.pipelinesSearch||'')}">
    <select class="field compact" id="pipelineSort">
      <option value="updated" ${state.pipelinesSort!=='name'?'selected':''}>Sort: Recently updated</option>
      <option value="name" ${state.pipelinesSort==='name'?'selected':''}>Sort: Name</option>
    </select>
    <button class="button" data-route="/runs">View all runs</button>
  </div>

  <div class="card pr-table-card pipe-table-card">
    <div class="pipe-table-head">
      <span>Status</span><span>Pipeline</span><span>Branch</span><span>Commit / Trigger</span><span>Stages</span><span>Duration</span><span>Updated</span><span></span>
    </div>
    <div id="pipelineList">
      ${!definitions.length?'<div class="empty">No pipeline definitions.</div>':filtered.length?sortedPipeRows(filtered,(state.pipelinesSort||'updated')).map(({def,latest})=>pipelineDashboardRow(def,latest)).join(''):'<div class="empty">No pipelines match this filter.</div>'}
    </div>
  </div>

  ${selected?`<div class="card" style="margin-top:16px"><div class="card-header"><h2>${esc(selected.name)}</h2><span class="pill">v${esc(selected.version)}</span></div>
    <div class="card-body">${(selected.jobs||[]).map((job,i)=>`<div class="side-stat"><span>${i+1}. ${esc(job.name)}</span><strong>${(job.steps||[]).length} steps · check: ${esc(job.checkName||job.name)}</strong></div>`).join('')}</div></div>`:''}

  <div class="pipe-footer-grid">
    <div class="card"><div class="card-header"><h2>Recent pipeline activity</h2><button class="button text" data-route="/runs">View all</button></div><div class="card-body">
      ${activity.length?activity.map(item=>`<div class="activity-item"><span class="avatar">${esc(item.initials)}</span><div><p><strong>${esc(item.actor)}</strong> ${esc(item.detail)}</p></div><small>${esc(item.when)}</small></div>`).join(''):'<div class="empty small">Run a pipeline to see activity here.</div>'}
    </div></div>
    <div class="card"><div class="card-header"><h2>Pipeline templates</h2></div><div class="card-body pipe-templates">
      ${templates.map(t=>`<div class="pipe-template"><span class="pipe-template-icon">${esc(t.icon)}</span><div><strong>${esc(t.name)}</strong><small>${esc(t.desc)}</small></div><button type="button" class="button compact" data-template="${esc(t.name)}">Use template</button></div>`).join('')}
    </div></div>
    <div class="card"><div class="card-header"><h2>Environments touched</h2></div><div class="card-body">
      <div class="env-row"><span class="env-dot ${state.local?.associated?'ok':'warn'}"></span><div><strong>Local</strong><small>${state.local?.associated?'Working copy linked':'Not linked'}</small></div><span class="pill">${state.local?.status?.currentBranch||'—'}</span></div>
      <div class="env-row"><span class="env-dot ${source()?'ok':'warn'}"></span><div><strong>GitHub</strong><small>${source()?esc(source().repositoryId.owner+'/'+source().repositoryId.name):'Not connected'}</small></div><span class="pill">${esc(source()?.defaultBranch||'—')}</span></div>
      <div class="env-row"><span class="env-dot ok"></span><div><strong>Simulated CI</strong><small>Pipeline execution mode</small></div><span class="pill">Active</span></div>
    </div></div>
  </div>`;

  el('refreshPipelines').onclick=()=>renderPipelines(route);
  el('runPipelineQuick').onclick=()=>{
    const first=definitions.find(d=>d.enabled)||definitions[0];
    if(first)openRunPipeline(first.id);
    else showToast('No pipeline definitions available',true);
  };
  el('newPipelineHint').onclick=()=>navigate('/pipelines/new');
  el('pipelineSearch').oninput=e=>{state.pipelinesSearch=e.target.value;renderPipelines(route)};
  el('pipelineSort').onchange=e=>{state.pipelinesSort=e.target.value;renderPipelines(route)};
  document.querySelectorAll('[data-pipe-filter]').forEach(btn=>btn.onclick=()=>{state.pipelinesFilter=btn.dataset.pipeFilter;renderPipelines(route)});
  document.querySelectorAll('[data-template]').forEach(btn=>btn.onclick=()=>navigate('/pipelines/new'));
  document.querySelectorAll('[data-run-pipeline]').forEach(button=>button.onclick=()=>openRunPipeline(button.dataset.runPipeline));
  document.querySelectorAll('[data-edit-pipeline]').forEach(button=>button.onclick=()=>navigate(`/pipelines/${button.dataset.editPipeline}/edit`));
  document.querySelectorAll('[data-view-pipeline]').forEach(button=>button.onclick=()=>navigate(`/pipelines/${button.dataset.viewPipeline}`));
  document.querySelectorAll('[data-toggle-pipeline]').forEach(button=>button.onclick=async()=>{
    try{
      const enabled=button.dataset.enabled==='1';
      await api(`/api/pipelines/definitions/${button.dataset.togglePipeline}/${enabled?'disable':'enable'}`,{method:'POST',body:'{}'});
      showToast(enabled?'Pipeline disabled':'Pipeline enabled');renderPipelines(route);
    }catch(error){showToast(error.message,true)}
  });
  document.querySelectorAll('[data-delete-pipeline]').forEach(button=>button.onclick=async()=>{
    if(!confirm('Delete this pipeline definition?'))return;
    try{await api(`/api/pipelines/definitions/${button.dataset.deletePipeline}`,{method:'DELETE'});showToast('Pipeline deleted');navigate('/pipelines')}catch(error){showToast(error.message,true)}
  });
}

function sortedPipeRows(items,sort='updated'){
  return items.slice().sort((a,b)=>{
    if(sort==='name')return String(a.def.name||'').localeCompare(String(b.def.name||''));
    const at=new Date(a.latest?.completedAt||a.latest?.startedAt||a.latest?.createdAt||0).getTime();
    const bt=new Date(b.latest?.completedAt||b.latest?.startedAt||b.latest?.createdAt||0).getTime();
    return bt-at||String(a.def.name||'').localeCompare(String(b.def.name||''));
  });
}

function pipelineStageDots(def,latest){
  const jobs=latest?.jobs?.length?latest.jobs:(def.jobs||[]).map(j=>({name:j.name,status:'Pending'}));
  return `<div class="pipe-stages">${jobs.map(j=>{
    const s=String(j.status||'Pending').toLowerCase();
    const cls=s.includes('succeed')||s==='passed'?'ok':s.includes('fail')||s==='cancelled'?'bad':s.includes('run')||s==='queued'||s==='assigned'?'run':'wait';
    return `<span class="pipe-stage ${cls}" title="${esc(j.name||'')} · ${esc(j.status||'Pending')}"></span>`;
  }).join('')}</div>`;
}

function pipelineDashboardRow(def,latest){
  const status=latest?.status||(def.enabled?'Scheduled':'Disabled');
  const tone=statusClass(status);
  const desc=`${triggerLabels(def.triggers)||'Manual'} · ${(def.jobs||[]).length} jobs · ${def.enabled?'enabled':'disabled'}`;
  const branch=latest?.ref||source()?.defaultBranch||'main';
  const commit=latest?.commitSha?String(latest.commitSha).slice(0,7):'—';
  const trigger=latest?.trigger||'Manual';
  const duration=latest?durationLabel(latest.startedAt,latest.completedAt):'—';
  const updated=latest?relativeTime(latest.completedAt||latest.startedAt||latest.createdAt):'—';
  return `<article class="pipeline-row pipe-row">
    <div class="pipe-status-cell"><span class="run-status ${tone}">${checkIcon(status)}</span></div>
    <div class="pipe-name-cell">
      <strong>${esc(def.name)}</strong>
      <small>${esc(desc)}</small>
    </div>
    <div class="pipe-branch-cell"><span class="branch-pill">${esc(branch)}</span></div>
    <div class="pipe-commit-cell">
      <span class="avatar sm">${esc(initials(latest?.requestedBy||def.name))}</span>
      <div><code>${esc(commit)}</code><small>${esc(trigger)}</small></div>
    </div>
    <div class="pipe-stages-cell">${pipelineStageDots(def,latest)}</div>
    <div class="pipe-duration-cell">${esc(duration)}</div>
    <div class="pipe-updated-cell">${esc(updated)}</div>
    <div class="pipe-actions-cell header-actions">
      <button class="button compact" data-view-pipeline="${esc(def.id)}">View</button>
      <button class="button compact" data-edit-pipeline="${esc(def.id)}">Edit</button>
      <button class="button compact" data-toggle-pipeline="${esc(def.id)}" data-enabled="${def.enabled?'1':'0'}">${def.enabled?'Disable':'Enable'}</button>
      <button class="button compact danger" data-delete-pipeline="${esc(def.id)}">Delete</button>
      <button class="button compact primary" data-run-pipeline="${esc(def.id)}" ${def.enabled?'':'disabled'}>Run</button>
    </div>
  </article>`;
}

async function renderPipelineBuilder(route){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  const parts=route.split('/').filter(Boolean);
  const editId=parts[0]==='pipelines'&&parts[2]==='edit'?parts[1]:null;
  let definition=null,yamlText='';
  // Every edit re-renders the builder; fetching here would leave an async gap that detaches the
  // controls mid-interaction, so the catalogue and the edited definition are loaded only once.
  if(!state.pipelineJobTemplates){
    try{state.pipelineJobTemplates=await api('/api/pipelines/job-templates')}catch{state.pipelineJobTemplates=[]}
  }
  const templates=state.pipelineJobTemplates;
  const isNewDraft=!state.pipelineDraft||state.pipelineDraft.editId!==(editId||'new');
  if(editId&&isNewDraft){
    definition=await api(`/api/pipelines/definitions/${editId}`);
    try{const y=await api(`/api/pipelines/definitions/${editId}/yaml`);yamlText=y.yaml||y||''}catch{yamlText=''}
  }
  if(isNewDraft){
    state.pipelineDraft={
      editId:editId||'new',
      name:definition?.name||'New pipeline',
      timeoutSeconds:definition?.timeoutSeconds||3600,
      triggers:(definition?.triggers||['Manual']).map(String),
      environment:{...(definition?.environment||{})},
      // Fields the visual editor does not surface are still carried so YAML round trips keep them.
      jobs:(definition?.jobs||[]).map(j=>({
        name:j.name,
        requiresCapabilities:j.requiresCapabilities||[],
        environment:{...(j.environment||{})},
        timeoutSeconds:j.timeoutSeconds||1800,
        publishCheck:!!j.publishCheck,
        checkName:j.checkName||'',
        artifactGlobs:j.artifactGlobs||[],
        continueOnError:!!j.continueOnError,
        steps:(j.steps||[]).map(s=>({
          name:s.name,
          command:s.command,
          shell:s.shell||'',
          environment:{...(s.environment||{})},
          timeoutSeconds:s.timeoutSeconds||300,
          continueOnError:!!s.continueOnError
        }))
      })),
      yaml:yamlText,
      tab:'visual'
    };
  }
  const draft=state.pipelineDraft;
  crumbs(`Projects <span>/</span> ${esc(state.context?.project?.name||'Project')} <span>/</span> Build <span>/</span> ${editId?'Edit pipeline':'New pipeline'}`);
  const triggerOpts=['Manual','Push','ChangeOpened','ChangeUpdated','ChangeMerged'];
  el('content').innerHTML=`
  <section class="pipeline-builder" id="pipelineBuilder">
    <header class="list-page-header">
      <div><h1>${editId?'Edit pipeline':'New pipeline'}</h1><p class="description">Declarative jobs with a visual editor or YAML.</p></div>
      <div class="header-actions">
        <button class="button" data-route="/pipelines">Cancel</button>
        <button class="button primary" id="pipelineBuilderSave">Save pipeline</button>
      </div>
    </header>
    <div class="pr-status-tabs" role="tablist">
      <button type="button" class="pr-status-tab ${draft.tab==='visual'?'active':''}" data-builder-tab="visual">Visual</button>
      <button type="button" class="pr-status-tab ${draft.tab==='yaml'?'active':''}" data-builder-tab="yaml">YAML</button>
    </div>
    <p class="builder-error" id="pipelineBuilderError" role="alert" hidden></p>
    <div class="pipeline-builder-grid">
      <div class="card">
        <div class="card-body settings-form" id="pipelineBuilderMain">
          ${draft.tab==='yaml'?`
            <label class="form-label" for="pipelineYaml">Pipeline YAML</label>
            <textarea class="field yaml-editor" id="pipelineYaml" rows="22">${esc(draft.yaml||'')}</textarea>
            <p class="description">Saved via the YAML API. Use Visual to insert premade jobs.</p>
          `:`
            <label class="form-label" for="pipelineName">Name</label>
            <input class="field" id="pipelineName" value="${esc(draft.name)}">
            <label class="form-label" for="pipelineTimeout">Timeout (seconds)</label>
            <input class="field" id="pipelineTimeout" type="number" min="60" value="${esc(draft.timeoutSeconds)}">
            <p class="form-label">Triggers</p>
            <div class="trigger-row">${triggerOpts.map(t=>`<label class="choice-row"><input type="checkbox" data-trigger="${t}" ${draft.triggers.map(String).some(x=>x.toLowerCase()===t.toLowerCase())?'checked':''}> ${t}</label>`).join('')}</div>
            <div class="jobs-editor" id="jobsEditor">
              ${(draft.jobs||[]).map((job,ji)=>`
                <div class="job-block" data-job-index="${ji}">
                  <div class="job-block-head">
                    <input class="field compact" data-job-name value="${esc(job.name)}" placeholder="Job name">
                    <button type="button" class="button compact" data-move-job="${ji}" data-dir="up" title="Move up" ${ji===0?'disabled':''}>↑</button>
                    <button type="button" class="button compact" data-move-job="${ji}" data-dir="down" title="Move down" ${ji===draft.jobs.length-1?'disabled':''}>↓</button>
                    <button type="button" class="button danger compact" data-remove-job="${ji}">Remove</button>
                  </div>
                  ${(job.steps||[]).map((step,si)=>`
                    <div class="step-row">
                      <input class="field compact" data-step-name data-ji="${ji}" data-si="${si}" value="${esc(step.name)}" placeholder="Step">
                      <input class="field compact" data-step-command data-ji="${ji}" data-si="${si}" value="${esc(step.command)}" placeholder="Command">
                      <button type="button" class="button danger compact" data-remove-step="${ji}:${si}" title="Remove step">✕</button>
                    </div>`).join('')||'<p class="description">No steps yet.</p>'}
                  <button type="button" class="button text" data-add-step="${ji}">＋ Add step</button>
                </div>`).join('')||'<div class="empty small">No jobs yet — insert a premade job from the catalogue.</div>'}
            </div>
            <button type="button" class="button" id="addEmptyJob">＋ Empty job</button>
          `}
        </div>
      </div>
      <aside class="card job-catalog">
        <div class="card-header"><h2>Premade jobs</h2></div>
        <div class="card-body">
          ${(templates||[]).map(t=>`
            <button type="button" class="job-template-card" data-insert-template="${esc(t.id)}">
              <strong>${esc(t.name)}</strong>
              <small>${esc(t.description||'')}</small>
            </button>`).join('')||'<div class="empty small">No templates loaded.</div>'}
        </div>
      </aside>
    </div>
  </section>`;

  const persistVisualFields=()=>{
    if(draft.tab!=='visual')return;
    // Assign even when blank, otherwise clearing the name silently restores the previous value.
    if(el('pipelineName'))draft.name=el('pipelineName').value;
    draft.timeoutSeconds=Number(el('pipelineTimeout')?.value||draft.timeoutSeconds);
    draft.triggers=[...document.querySelectorAll('[data-trigger]:checked')].map(i=>i.dataset.trigger);
    document.querySelectorAll('[data-job-name]').forEach((input,idx)=>{if(draft.jobs[idx])draft.jobs[idx].name=input.value});
    document.querySelectorAll('[data-step-name]').forEach(input=>{
      const ji=+input.dataset.ji,si=+input.dataset.si;
      if(draft.jobs[ji]?.steps[si])draft.jobs[ji].steps[si].name=input.value;
    });
    document.querySelectorAll('[data-step-command]').forEach(input=>{
      const ji=+input.dataset.ji,si=+input.dataset.si;
      if(draft.jobs[ji]?.steps[si])draft.jobs[ji].steps[si].command=input.value;
    });
  };

  document.querySelectorAll('[data-builder-tab]').forEach(btn=>btn.onclick=async()=>{
    const next=btn.dataset.builderTab;
    persistVisualFields();
    if(draft.tab==='yaml'){
      draft.yaml=el('pipelineYaml')?.value||draft.yaml;
      // Leaving the YAML tab folds the document back into the visual model so the two never drift.
      if(next==='visual'&&(draft.yaml||'').trim()){
        try{
          const parsed=await api('/api/pipelines/definitions/validate-yaml',{method:'POST',body:JSON.stringify({yaml:draft.yaml})});
          applyDefinitionToDraft(draft,parsed.definition);
        }catch(error){showBuilderError(error.message);return}
      }
    }else if(next==='yaml'){
      try{
        const rendered=await api('/api/pipelines/definitions/to-yaml',{method:'POST',body:JSON.stringify(pipelineDraftBody(draft))});
        draft.yaml=rendered.yaml||draft.yaml;
      }catch{/* keep the last known document rather than blanking the editor */}
    }
    draft.tab=next;
    renderPipelineBuilder(route);
  });
  document.querySelectorAll('[data-move-job]').forEach(btn=>btn.onclick=()=>{
    persistVisualFields();
    const from=+btn.dataset.moveJob,to=btn.dataset.dir==='up'?from-1:from+1;
    if(to<0||to>=draft.jobs.length)return;
    const [moved]=draft.jobs.splice(from,1);
    draft.jobs.splice(to,0,moved);
    renderPipelineBuilder(route);
  });
  document.querySelectorAll('[data-remove-step]').forEach(btn=>btn.onclick=()=>{
    persistVisualFields();
    const [ji,si]=btn.dataset.removeStep.split(':').map(Number);
    draft.jobs[ji]?.steps.splice(si,1);
    renderPipelineBuilder(route);
  });
  el('addEmptyJob')?.addEventListener('click',()=>{
    persistVisualFields();
    draft.jobs.push({name:'Job',requiresCapabilities:[],publishCheck:false,checkName:'',steps:[{name:'Run',command:'echo hello',shell:'',timeoutSeconds:300}]});
    renderPipelineBuilder(route);
  });
  document.querySelectorAll('[data-remove-job]').forEach(btn=>btn.onclick=()=>{
    persistVisualFields();
    draft.jobs.splice(+btn.dataset.removeJob,1);
    renderPipelineBuilder(route);
  });
  document.querySelectorAll('[data-add-step]').forEach(btn=>btn.onclick=()=>{
    persistVisualFields();
    draft.jobs[+btn.dataset.addStep].steps.push({name:'Step',command:'',shell:'',timeoutSeconds:300});
    renderPipelineBuilder(route);
  });
  document.querySelectorAll('[data-insert-template]').forEach((btn,index)=>btn.onclick=()=>{
    persistVisualFields();
    const template=(templates||[])[index]||(templates||[]).find(t=>String(t.id)===String(btn.dataset.insertTemplate));
    const job=template?.job||template?.Job;
    if(!job){
      showToast('Could not load that job template',true);
      return;
    }
    draft.jobs.push({
      name:job.name||job.Name||template.name,
      requiresCapabilities:job.requiresCapabilities||job.RequiresCapabilities||[],
      environment:{...(job.environment||job.Environment||{})},
      timeoutSeconds:job.timeoutSeconds||job.TimeoutSeconds||1800,
      publishCheck:!!(job.publishCheck??job.PublishCheck),
      checkName:job.checkName||job.CheckName||'',
      artifactGlobs:job.artifactGlobs||job.ArtifactGlobs||[],
      continueOnError:!!(job.continueOnError??job.ContinueOnError),
      steps:(job.steps||job.Steps||[{name:'Run',command:'echo ok'}]).map(s=>({
        name:s.name||s.Name,
        command:s.command||s.Command,
        shell:s.shell||s.Shell||'',
        environment:{...(s.environment||s.Environment||{})},
        timeoutSeconds:s.timeoutSeconds||s.TimeoutSeconds||300,
        continueOnError:!!(s.continueOnError??s.ContinueOnError)
      }))
    });
    draft.tab='visual';
    renderPipelineBuilder(route);
  });
  el('pipelineBuilderSave').onclick=async()=>{
    try{
      if(draft.tab==='yaml'){
        const yaml=el('pipelineYaml')?.value??draft.yaml;
        draft.yaml=yaml;
        if(editId)await api(`/api/pipelines/definitions/${editId}/yaml`,{method:'PUT',body:JSON.stringify({yaml})});
        else await api('/api/pipelines/definitions/from-yaml',{method:'POST',body:JSON.stringify({yaml})});
      }else{
        persistVisualFields();
        const problem=pipelineDraftProblem(draft);
        if(problem)return showBuilderError(problem);
        const body=pipelineDraftBody(draft);
        if(editId)await api(`/api/pipelines/definitions/${editId}`,{method:'PUT',body:JSON.stringify(body)});
        else await api('/api/pipelines/definitions',{method:'POST',body:JSON.stringify(body)});
      }
      state.pipelineDraft=null;
      showToast('Pipeline saved');
      navigate('/pipelines');
    }catch(error){showBuilderError(error.message)}
  };
}

function showBuilderError(message){
  const box=el('pipelineBuilderError');
  if(box){box.textContent=message||'';box.hidden=!message}
  if(message)showToast(message,true);
}

/// Mirrors the server-side validation so obvious mistakes never need a round trip.
function pipelineDraftProblem(draft){
  if(!String(draft.name||'').trim())return 'Pipeline name is required.';
  if(!(draft.jobs||[]).length)return 'A pipeline needs at least one job.';
  for(const job of draft.jobs){
    if(!String(job.name||'').trim())return 'Every job requires a name.';
    if(!(job.steps||[]).length)return `Job “${job.name}” needs at least one step.`;
    if(job.steps.some(step=>!String(step.command||'').trim()))return `Every step in “${job.name}” requires a command.`;
  }
  return null;
}

function pipelineDraftBody(draft){
  return {
    name:String(draft.name||'').trim(),
    timeoutSeconds:Number(draft.timeoutSeconds)||3600,
    environment:draft.environment||{},
    triggers:(draft.triggers||[]).length?draft.triggers:['Manual'],
    jobs:(draft.jobs||[]).map(job=>({
      name:String(job.name||'').trim(),
      requiresCapabilities:job.requiresCapabilities||[],
      environment:job.environment||{},
      timeoutSeconds:Number(job.timeoutSeconds)||1800,
      publishCheck:!!job.publishCheck,
      checkName:job.checkName||null,
      artifactGlobs:job.artifactGlobs||[],
      continueOnError:!!job.continueOnError,
      steps:(job.steps||[]).map(step=>({
        name:step.name||step.command||'',
        command:step.command||'',
        shell:step.shell||null,
        environment:step.environment||{},
        timeoutSeconds:Number(step.timeoutSeconds)||300,
        continueOnError:!!step.continueOnError
      }))
    }))
  };
}

function applyDefinitionToDraft(draft,definition){
  if(!definition)return draft;
  draft.name=definition.name||'';
  draft.timeoutSeconds=definition.timeoutSeconds||3600;
  draft.environment={...(definition.environment||{})};
  draft.triggers=(definition.triggers||[]).map(String);
  draft.jobs=(definition.jobs||[]).map(job=>({
    name:job.name||'',
    requiresCapabilities:job.requiresCapabilities||[],
    environment:{...(job.environment||{})},
    timeoutSeconds:job.timeoutSeconds||1800,
    publishCheck:!!job.publishCheck,
    checkName:job.checkName||'',
    artifactGlobs:job.artifactGlobs||[],
    continueOnError:!!job.continueOnError,
    steps:(job.steps||[]).map(step=>({
      name:step.name||'',
      command:step.command||'',
      shell:step.shell||'',
      timeoutSeconds:step.timeoutSeconds||300,
      continueOnError:!!step.continueOnError,
      environment:{...(step.environment||{})}
    }))
  }));
  return draft;
}

function openRunPipeline(definitionId){
  const head=(state.local?.status?.headSha)||'';
  openModal(`<div class="modal-content"><h2>Run pipeline</h2>
    <p class="description">Provide the exact commit SHA to validate. The runner checks out this SHA in an isolated workspace.</p>
    <label class="form-label" for="runRef">Branch / ref</label><input class="field" id="runRef" value="${esc(state.local?.status?.currentBranch||'main')}">
    <label class="form-label" for="runCommit">Commit SHA</label><input class="field" id="runCommit" value="${esc(head)}" placeholder="full or short SHA" required>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Start run</button></div></div>`,async event=>{
    if(event.submitter?.value!=='submit')return;
    const commitSha=(el('runCommit').value||'').trim();
    if(!commitSha||commitSha.toLowerCase()==='local'){showToast('Enter a real commit SHA',true);return}
    try{
      const run=await api('/api/pipelines/runs',{method:'POST',body:JSON.stringify({
        definitionId,
        ref:el('runRef').value||'main',
        commitSha,
        repositoryUrl:state.connections[0]?.url||state.local?.status?.originUrl||null
      })});
      showToast('Pipeline run started');navigate(`/runs/${run.id}`);
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

async function renderRuns(){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  const runs=await api('/api/pipelines/runs');
  crumbs(projectCrumb('Build <span>/</span> Runs'));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Runs</h1><p>Recent build pipeline executions.</p></div>
    <button class="button" id="refreshRuns">Refresh</button></div>
    <div class="card">${runs.map(run=>`<article class="run-row" data-route="/runs/${esc(run.id)}">
      <span class="run-status ${statusClass(run.status)}">${checkIcon(run.status)}</span>
      <div><h3>${esc(run.definitionName)} <code>${esc(shortId(run.id))}</code></h3>
        <p>${esc(run.trigger)} · ${esc(run.ref)} · <code>${esc((run.commitSha||'').slice(0,7))}</code></p></div>
      <span class="status ${statusClass(run.status)}">${esc(run.status)}</span>
    </article>`).join('')||'<div class="empty">No runs yet.</div>'}</div>`;
  el('refreshRuns').onclick=renderRuns;
}

async function renderRunDetail(runId){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  clearInterval(state.runPoll);
  const run=await api(`/api/pipelines/runs/${runId}`);
  const running=isActiveStatus(run.status)||(run.jobs||[]).some(j=>isActiveStatus(j.status));
  const failedJob=(run.jobs||[]).find(j=>j.status==='Failed');
  crumbs(projectCrumb(`Build <span>/</span> <button class="button text" data-route="/runs">Runs</button> <span>/</span> ${esc(shortId(run.id))}`));
  el('content').innerHTML=`<div class="run-summary">
    <div class="list-page-header">
      <div>
        <h1>${esc(run.definitionName)}</h1>
        <p><code>${esc(run.id)}</code> · ${esc(run.trigger)} · ${esc(run.ref)} · <code>${esc((run.commitSha||'').slice(0,12))}</code>
          · <span class="status ${statusClass(run.status)}">${esc(run.status)}</span>${running?' · <span class="check-state running">Live</span>':''}</p>
      </div>
      <div class="header-actions">
        <button class="button" id="refreshRun">Refresh</button>
        <button class="button" id="retryRun">Retry</button>
        <button class="button danger" id="cancelRun" ${['Succeeded','Failed','Cancelled','PartiallySucceeded'].includes(run.status)?'disabled':''}>Cancel</button>
      </div>
    </div>
    ${failedJob?`<div class="log-error-banner run-fail-banner">
      <strong>Failed</strong>
      <span>${esc(failedJob.name)}${failedJob.failureReason?` — ${esc(failedJob.failureReason)}`:''}</span>
      <button type="button" class="button primary" data-route="/runs/${esc(runId)}/jobs/${esc(failedJob.id)}">View logs</button>
    </div>`:''}
    <div class="card job-list-card">
      <div class="card-header"><h2>Jobs</h2><span class="muted-hint">Click a job to open its log</span></div>
      ${(run.jobs||[]).map(j=>`<a class="job-list-row ${statusClass(j.status)}" href="#/runs/${esc(runId)}/jobs/${esc(j.id)}" data-route="/runs/${esc(runId)}/jobs/${esc(j.id)}">
        <span class="run-status ${statusClass(j.status)}">${checkIcon(j.status)}</span>
        <div class="job-list-copy">
          <h3>${esc(j.name)}</h3>
          <p>${esc(j.status)} · ${esc(durationLabel(j.startedAt,j.completedAt))}${j.failureReason?` · ${esc(j.failureReason)}`:''}</p>
        </div>
        <span class="status ${statusClass(j.status)}">${esc(j.status)}</span>
        <span class="job-list-open">View log</span>
      </a>`).join('')||'<div class="empty">No jobs.</div>'}
    </div>
  </div>`;
  el('refreshRun').onclick=()=>renderRunDetail(runId);
  el('retryRun').onclick=async()=>{try{const next=await api(`/api/pipelines/runs/${runId}/retry`,{method:'POST',body:'{}'});showToast('Retry started');navigate(`/runs/${next.id}`)}catch(error){showToast(error.message,true)}};
  el('cancelRun').onclick=async()=>{try{await api(`/api/pipelines/runs/${runId}/cancel`,{method:'POST',body:JSON.stringify({})});showToast('Run cancelled');renderRunDetail(runId)}catch(error){showToast(error.message,true)}};
  if(running && state.route===`/runs/${runId}`){
    state.runPoll=setInterval(()=>{if(state.route===`/runs/${runId}`)renderRunDetail(runId);else clearInterval(state.runPoll)},2500);
  }
}

function isActiveStatus(status){
  return ['Queued','Running','WaitingForRunner','Assigned'].includes(status);
}

function isErrorLogLine(line){
  const stream=String(line.stream||'stdout').toLowerCase();
  return stream==='stderr'||/\berror\b|\bfailed\b|exception|:\s*error\s/i.test(line.message||'');
}

function formatLogLines(logs, startIndex=1){
  if(!logs?.length)return '<div class="empty small">No output for this step.</div>';
  return logs.map((line,i)=>{
    const n=startIndex+i;
    const stream=String(line.stream||'stdout').toLowerCase();
    const err=isErrorLogLine(line);
    return `<div class="log-line${err?' is-error':''}" data-stream="${esc(stream)}" id="L${n}"><span class="log-num" title="Line ${n}">${n}</span><span class="log-msg">${esc(line.message)}</span></div>`;
  }).join('');
}

function logsToPlainText(logs){
  return (logs||[]).map(line=>{
    const t=line.timestamp?new Date(line.timestamp).toISOString():'';
    return `[${t}] [${line.stream||'stdout'}] ${line.message??''}`;
  }).join('\n');
}

function stepShouldOpen(step, logs){
  if(isActiveStatus(step.status)||step.status==='Failed'||step.status==='Cancelled')return true;
  if((logs||[]).some(isErrorLogLine))return true;
  return false;
}

async function renderJobPage(runId, jobId){
  if(!hasModule('pipelines'))return renderError(new Error('Build module is not enabled.'));
  clearInterval(state.runPoll);
  const run=await api(`/api/pipelines/runs/${runId}`);
  const job=run.jobs?.find(j=>j.id===jobId);
  if(!job)return renderError(new Error('Job was not found on this run.'));
  state.selectedJobId=jobId;
  const query=(state.logQuery||'').trim().toLowerCase();
  const errorsOnly=!!state.logErrorsOnly;
  const wrap=state.logWrap!==false;
  const running=isActiveStatus(job.status);
  const allLogs=job.logs||[];
  const visibleLogs=allLogs.filter(line=>{
    if(errorsOnly&&!isErrorLogLine(line))return false;
    if(query&&!(line.message||'').toLowerCase().includes(query))return false;
    return true;
  });
  const steps=[...(job.steps||[])].sort((a,b)=>(a.ordinal??0)-(b.ordinal??0));
  const stepIds=new Set(steps.map(s=>s.id));
  const orphanLogs=visibleLogs.filter(l=>!l.stepId||!stepIds.has(l.stepId));
  const failedCases=(job.testResults?.suites||[]).flatMap(s=>(s.cases||[]).filter(c=>String(c.outcome).toLowerCase()==='failed'||c.outcome===1));
  let lineCounter=1;

  function renderStepBlock(id, name, status, duration, command, logs, forceOpen){
    const open=forceOpen||stepShouldOpen({status}, logs);
    const body=formatLogLines(logs, lineCounter);
    const start=lineCounter;
    lineCounter+=logs.length||0;
    return `<details class="log-step ${statusClass(status)}" ${open?'open':''} data-step-id="${esc(id||'setup')}">
      <summary class="log-step-summary">
        <span class="run-status ${statusClass(status)}">${checkIcon(status)}</span>
        <span class="log-step-title">
          <strong>${esc(name)}</strong>
          ${command?`<code>${esc(command)}</code>`:''}
        </span>
        <span class="log-step-meta">${esc(status)} · ${esc(duration)} · ${logs.length} lines</span>
      </summary>
      <div class="log-step-body ${wrap?'is-wrap':''}" data-line-start="${start}">${body}</div>
    </details>`;
  }

  const stepBlocks=steps.map(step=>{
    const logs=visibleLogs.filter(l=>l.stepId===step.id);
    return renderStepBlock(step.id, step.name, step.status, durationLabel(step.startedAt, step.completedAt), step.command, logs);
  }).join('');
  const setupBlock=orphanLogs.length||!steps.length
    ? renderStepBlock(null, steps.length?'Job output':'Output', job.status, durationLabel(job.startedAt, job.completedAt), null, orphanLogs.length?orphanLogs:visibleLogs, true)
    : '';

  crumbs(projectCrumb(`Build <span>/</span> <button class="button text" data-route="/runs">Runs</button> <span>/</span> <button class="button text" data-route="/runs/${esc(runId)}">${esc(shortId(runId))}</button> <span>/</span> ${esc(job.name)}`));
  el('content').innerHTML=`<div class="job-page">
    <header class="job-page-header">
      <div class="job-page-heading">
        <button type="button" class="button text" data-route="/runs/${esc(runId)}">← ${esc(run.definitionName)}</button>
        <h1>${esc(job.name)}</h1>
        <p>
          <span class="status ${statusClass(job.status)}">${esc(job.status)}</span>
          · ${esc(durationLabel(job.startedAt,job.completedAt))}
          ${job.exitCode!=null?` · exit ${esc(job.exitCode)}`:''}
          ${running?' · <span class="check-state running">Live</span>':''}
        </p>
      </div>
      <div class="header-actions">
        <button class="button" id="refreshJobPage">Refresh</button>
        <button class="button" id="copyJobLogs">Copy</button>
        <button class="button primary" id="downloadJobLogs">Download</button>
      </div>
    </header>
    ${job.failureReason?`<div class="log-error-banner"><strong>Failure</strong><span>${esc(job.failureReason)}</span></div>`:''}
    <div class="job-page-layout">
      <aside class="job-rail">
        <div class="card">
          <div class="card-header"><h2>Jobs in run</h2></div>
          ${(run.jobs||[]).map(j=>`<a class="job-rail-item ${j.id===job.id?'active':''} ${statusClass(j.status)}" href="#/runs/${esc(runId)}/jobs/${esc(j.id)}" data-route="/runs/${esc(runId)}/jobs/${esc(j.id)}">
            <span class="run-status ${statusClass(j.status)}">${checkIcon(j.status)}</span>
            <span>${esc(j.name)}</span>
          </a>`).join('')}
        </div>
        <div class="card" style="margin-top:14px">
          <div class="card-header"><h2>Details</h2></div>
          <div class="card-body">
            <div class="side-stat"><span>Duration</span><strong>${esc(durationLabel(job.startedAt,job.completedAt))}</strong></div>
            <div class="side-stat"><span>Exit code</span><strong>${job.exitCode==null?'—':esc(job.exitCode)}</strong></div>
            ${job.testResults?`<div class="side-stat"><span>Tests</span><strong>${job.testResults.passed}✓ ${job.testResults.failed}✗</strong></div>`:''}
            ${(job.artifacts||[]).map(a=>`<div class="side-stat"><span>${esc(a.name)}</span><button type="button" class="button text" data-download-artifact="${esc(a.id)}" data-job="${esc(job.id)}" data-name="${esc(a.name)}">Download</button></div>`).join('')}
            ${failedCases.length?`<div style="margin-top:10px"><strong style="font-size:.75rem;color:var(--red)">Failed tests</strong>${failedCases.map(c=>`<div class="failed-test"><strong>${esc(c.name)}</strong><small>${esc(c.errorMessage||'Failed')}</small></div>`).join('')}</div>`:''}
          </div>
        </div>
      </aside>
      <section class="job-log-panel">
        <div class="job-log-toolbar">
          <input class="field compact" id="logSearch" type="search" placeholder="Search logs…" value="${esc(state.logQuery||'')}">
          <button type="button" class="button${errorsOnly?' primary':''}" id="toggleErrorsOnly">Errors only</button>
          <button type="button" class="button${wrap?' primary':''}" id="toggleWrap">Wrap</button>
          <button type="button" class="button" id="expandAllSteps">Expand all</button>
          <button type="button" class="button" id="collapseAllSteps">Collapse</button>
          <span class="log-count">${visibleLogs.length} / ${allLogs.length} lines</span>
        </div>
        <div class="job-log-stream" id="jobLogStream">${setupBlock}${stepBlocks||'<div class="empty">No steps yet.</div>'}</div>
      </section>
    </div>
  </div>`;

  el('refreshJobPage').onclick=()=>renderJobPage(runId,jobId);
  el('copyJobLogs').onclick=async()=>{
    try{await navigator.clipboard.writeText(logsToPlainText(visibleLogs.length?visibleLogs:allLogs));showToast('Logs copied')}
    catch{showToast('Clipboard unavailable',true)}
  };
  el('downloadJobLogs').onclick=()=>{
    const blob=new Blob([logsToPlainText(allLogs)],{type:'text/plain;charset=utf-8'});
    const url=URL.createObjectURL(blob);
    const a=document.createElement('a');a.href=url;a.download=`${job.name||'job'}-${shortId(runId)}.log.txt`;a.click();URL.revokeObjectURL(url);
  };
  el('toggleErrorsOnly').onclick=()=>{state.logErrorsOnly=!state.logErrorsOnly;renderJobPage(runId,jobId)};
  el('toggleWrap').onclick=()=>{state.logWrap=!(state.logWrap!==false);renderJobPage(runId,jobId)};
  el('expandAllSteps').onclick=()=>document.querySelectorAll('.log-step').forEach(d=>d.open=true);
  el('collapseAllSteps').onclick=()=>document.querySelectorAll('.log-step').forEach(d=>d.open=false);
  el('logSearch').oninput=event=>{
    state.logQuery=event.target.value;
    clearTimeout(state.logSearchTimer);
    state.logSearchTimer=setTimeout(()=>renderJobPage(runId,jobId),200);
  };
  document.querySelectorAll('[data-download-artifact]').forEach(button=>button.onclick=()=>downloadArtifact(runId,button.dataset.job,button.dataset.downloadArtifact,button.dataset.name));

  const stream=el('jobLogStream');
  if(stream){
    if(running)stream.scrollTop=stream.scrollHeight;
    else{
      const firstErr=stream.querySelector('.log-line.is-error');
      if(firstErr)firstErr.scrollIntoView({block:'center'});
    }
  }
  if(running && /\/jobs\//.test(state.route)){
    state.runPoll=setInterval(()=>{if(/\/jobs\//.test(state.route))renderJobPage(runId,jobId);else clearInterval(state.runPoll)},2500);
  }
}

async function downloadArtifact(runId,jobId,artifactId,name){
  try{
    const response=await fetch(`/api/pipelines/runs/${runId}/jobs/${jobId}/artifacts/${artifactId}`,{headers:state.token?{Authorization:`Bearer ${state.token}`}:{}});
    if(!response.ok)throw new Error(`Download failed (${response.status})`);
    const blob=await response.blob();
    const url=URL.createObjectURL(blob);
    const a=document.createElement('a');a.href=url;a.download=name||'artifact';a.click();URL.revokeObjectURL(url);
  }catch(error){showToast(error.message,true)}
}

async function renderRunners(){
  return navigate('/organisation/settings/build');
}

function openAddRunner(){
  openModal(`<div class="modal-content"><h2>Add runner</h2>
    <p>Create a one-time registration token. Store it securely — it cannot be retrieved again.</p>
    <label class="form-label" for="tokenHours">Lifetime (hours)</label><input class="field" id="tokenHours" type="number" min="1" max="168" value="24">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create token</button></div></div>`,async event=>{
    if(event.submitter?.value!=='submit')return;
    try{
      const hours=Number(el('tokenHours').value)||24;
      const result=await api('/api/pipelines/runners/registration-tokens',{method:'POST',body:JSON.stringify({lifetimeHours:hours})});
      const port=location.port||'5262';
      openModal(`<div class="modal-content"><h2>Registration token</h2>
        <div class="policy-box" style="border-color:#5a4825;background:#211b0c;color:var(--amber)">Warning: copy this token now. It will not be shown again.</div>
        <label class="form-label">Token</label><input class="field" id="runnerTokenValue" value="${esc(result.token)}" readonly>
        <label class="form-label">Example CLI</label>
        <pre class="run-log" style="min-height:auto;padding:12px">dotnet run --project src/PipelineRunner --launch-profile Local
# first registration:
dotnet run --project src/PipelineRunner -- --url http://localhost:${esc(port)} --token ${esc(result.token)} --name My-PC</pre>
        <div class="modal-actions"><button class="button primary" value="close">Done</button></div></div>`);
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

async function mutate(path,body,message,method='POST'){
  try{
    const options={method};
    if(method!=='DELETE')options.body=JSON.stringify(body??{});
    state.change=await api(path,options);
    state.changes=await api('/api/review/changes');
    showToast(message);renderChange();
  }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

async function refreshActiveChange(notify=false){
  if(!state.change||!state.route.startsWith('/changes/'))return;
  try{
    state.change=await api(`/api/review/changes/${state.change.id}/refresh`,{method:'POST',body:'{}'});
    if(notify)showToast('GitHub state refreshed');
    renderChange();
  }catch(error){if(notify)showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

async function addComment(){
  const input=el('commentText');
  if(input.value.trim())await mutate(`/api/review/changes/${state.change.id}/comments`,{body:input.value.trim()},'Comment added');
}

function openModal(html,onclose){
  const modal=el('modal');
  el('modalBody').innerHTML=html;
  modal._submitter=null;
  el('modalForm').onsubmit=event=>modal._submitter=event.submitter;
  modal.onclose=()=>onclose?.({submitter:modal._submitter});
  modal.showModal();
}

function openImport(){
  if(!source())return showToast('Connect a repository first.',true);
  openModal(`<div class="modal-content"><h2>Import GitHub pull request</h2><p>${esc(source().repositoryId.owner)}/${esc(source().repositoryId.name)}</p>
    <label class="form-label" for="externalId">Pull request number</label><input class="field" id="externalId" inputmode="numeric" placeholder="24">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Import</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')try{
      const change=await api('/api/review/changes/import',{method:'POST',body:JSON.stringify({repositoryConnectionId:source().id,externalId:el('externalId').value})});
      state.changes=await api('/api/review/changes');navigate(`/changes/${change.id}`);
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

async function openDiscover(){
  if(!source())return showToast('Connect a repository first.',true);
  try{
    const external=await api(`/api/review/external-changes?repositoryConnectionId=${encodeURIComponent(source().id)}`);
    const known=new Set(state.changes.map(change=>String(change.externalId)));
    const fresh=external.filter(item=>!known.has(String(item.externalId)));
    if(!fresh.length)return showToast(external.length?'All discovered PRs are already imported.':'No open pull requests found.');
    openModal(`<div class="modal-content"><h2>Discover pull requests</h2><p>${fresh.length} new PR${fresh.length===1?'':'s'} ready to import.</p>
      <div style="max-height:240px;overflow:auto;margin-top:12px">${fresh.map(item=>`<label class="comment" style="display:flex;gap:10px;align-items:flex-start;border:0;padding:8px 0">
        <input type="checkbox" name="discover" value="${esc(item.externalId)}" checked>
        <span><strong>#${esc(item.externalNumber)}</strong> ${esc(item.title)}<br><small>${esc(item.author)} · ${esc(item.sourceBranch)} → ${esc(item.targetBranch)}</small></span>
      </label>`).join('')}</div>
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Import selected</button></div></div>`,async event=>{
      if(event.submitter?.value!=='submit')return;
      const selected=[...document.querySelectorAll('input[name="discover"]:checked')].map(input=>input.value);
      let imported=0;
      for(const externalId of selected){
        if(known.has(String(externalId)))continue;
        try{
          await api('/api/review/changes/import',{method:'POST',body:JSON.stringify({repositoryConnectionId:source().id,externalId})});
          known.add(String(externalId));imported++;
        }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
      }
      state.changes=await api('/api/review/changes');
      renderChanges();
      showToast(imported?`Imported ${imported} change${imported===1?'':'s'}`:'No new changes imported');
    });
  }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

function openCreateChange(branch){
  openModal(`<div class="modal-content"><h2>Create Change</h2><p>This creates a GitHub pull request.</p>
    <label class="form-label">Source</label><input class="field" id="sourceBranch" value="${esc(branch)}" readonly>
    <label class="form-label">Target</label><input class="field" id="targetBranch" value="${esc(source().defaultBranch)}">
    <label class="form-label">Title</label><input class="field" id="changeTitle" value="Review ${esc(branch)}">
    <label class="form-label">Description</label><textarea class="field" id="changeDescription" rows="4"></textarea>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create on GitHub</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')try{
      const change=await api('/api/review/changes',{method:'POST',body:JSON.stringify({repositoryConnectionId:source().id,sourceBranch:branch,targetBranch:el('targetBranch').value,title:el('changeTitle').value,description:el('changeDescription').value})});
      state.changes=await api('/api/review/changes');navigate(`/changes/${change.id}`);
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

function openReview(){
  openModal(`<div class="modal-content"><h2>Review changes</h2><p>Submit an overall review with an optional summary.</p>
    <label class="form-label" for="reviewBody">Comment</label><textarea class="field" id="reviewBody" rows="4"></textarea>
    <label class="form-label" for="reviewState">Decision</label><select class="field" id="reviewState"><option value="Comment">Comment</option><option value="Approved">Approve</option><option value="ChangesRequested">Request changes</option></select>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Submit review</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/reviews`,{state:el('reviewState').value,body:el('reviewBody').value},'Review submitted');
  });
}

function openMergeConfirm(){
  const c=state.change;
  openModal(`<div class="modal-content"><h2>Merge change #${esc(c.externalNumber||c.externalId)}?</h2>
    <p>This merges <strong>${esc(c.sourceBranch)}</strong> into <strong>${esc(c.targetBranch)}</strong> on GitHub. The operation cannot be undone from ForgeDeck.</p>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Confirm merge</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${c.id}/merge`,{},'Change merged on GitHub');
  });
}

function openInlineComment(file,line){
  openModal(`<div class="modal-content"><h2>Comment on line ${line}</h2><p><code>${esc(file)}</code> · ${esc(state.change.headCommit.slice(0,7))}</p>
    <textarea class="field" id="inlineBody" rows="4" placeholder="Leave actionable feedback…"></textarea>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Comment</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/comments`,{body:el('inlineBody').value,file,side:'right',line,commitSha:state.change.headCommit},'Inline comment added');
  });
}

function openReply(parentId){
  openModal(`<div class="modal-content"><h2>Reply</h2><textarea class="field" id="replyBody" rows="4"></textarea>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Reply</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/comments`,{body:el('replyBody').value,parentId},'Reply added');
  });
}

function openEditComment(commentId){
  const comment=state.change.comments.find(item=>item.id===commentId);
  if(!comment)return;
  openModal(`<div class="modal-content"><h2>Edit comment</h2><textarea class="field" id="editBody" rows="4">${esc(comment.body)}</textarea>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Save</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/comments/${commentId}`,{body:el('editBody').value},'Comment updated','PUT');
  });
}

async function deleteComment(commentId){
  openModal(`<div class="modal-content"><h2>Delete comment?</h2><p>This removes the comment from the platform-owned discussion history.</p>
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button danger" value="submit">Delete</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/comments/${commentId}`,undefined,'Comment deleted','DELETE');
  });
}

function openReviewer(){
  openModal(`<div class="modal-content"><h2>Request reviewer</h2><label class="form-label" for="reviewerName">Reviewer</label>
    <input class="field" id="reviewerName" placeholder="GitHub username or team member">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Request</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')await mutate(`/api/review/changes/${state.change.id}/reviewers`,{name:el('reviewerName').value},'Reviewer requested');
  });
}

function openConnect(){
  openModal(`<div class="modal-content"><h2>Connect repository</h2>
    <label class="form-label">Provider</label><input class="field" value="GitHub" readonly>
    <label class="form-label" for="repositoryUrl">Repository URL</label><input class="field" id="repositoryUrl" value="https://github.com/rowan-smith/upgraded-octo-parakeet">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Connect</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')try{
      await api('/api/source/repositories',{method:'POST',body:JSON.stringify({providerId:'github',url:el('repositoryUrl').value})});
      state.connections=await api('/api/source/repositories');showToast('Repository connected');refreshSettingsAfterSourceChange();
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

function openConnectLocal(){
  openModal(`<div class="modal-content"><h2>Connect local repository</h2>
    <p>Detect a local working copy and optionally associate its GitHub remote.</p>
    <label class="form-label" for="localPath">Path</label><input class="field" id="localPath" value="${esc(DEFAULT_LOCAL_PATH)}">
    <div id="detectPreview" class="policy-box" style="display:none;margin-top:14px"></div>
    <div class="modal-actions">
      <button class="button" value="cancel">Cancel</button>
      <button class="button" type="button" id="detectLocal">Detect</button>
      <button class="button primary" value="submit">Associate</button>
    </div></div>`,async event=>{
    if(event.submitter?.value!=='submit')return;
    const path=el('localPath')?.value?.trim()||DEFAULT_LOCAL_PATH;
    try{
      await api('/api/projects/current/local-repository',{method:'POST',body:JSON.stringify({path,connectGitHub:true})});
      state.local=await api('/api/projects/current/local-repository');
      state.connections=await api('/api/source/repositories');
      showToast('Local repository associated');
      refreshSettingsAfterSourceChange();
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
  el('detectLocal').onclick=async()=>{
    const path=el('localPath')?.value?.trim()||DEFAULT_LOCAL_PATH;
    try{
      const info=await api('/api/projects/current/local-repository/detect',{method:'POST',body:JSON.stringify({path})});
      const preview=el('detectPreview');
      preview.style.display='block';
      preview.innerHTML=info.isGitRepository
        ?`<strong>Detected</strong><br>${esc(info.root)}<br>${esc(info.currentBranch||'HEAD')} · ${(info.headSha||'').slice(0,7)} · ${info.isClean?'clean':`${info.modifiedFileCount} modified`}<br>${esc(info.detectedGitHub?.fullName||info.originUrl||'No GitHub remote detected')}`
        :`<strong>Not a git repository</strong><br>${esc(info.path)}`;
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  };
}

async function disconnectLocalRepository(){
  try{
    await api('/api/projects/current/local-repository',{method:'DELETE'});
    state.local=await api('/api/projects/current/local-repository');
    showToast('Local repository disconnected');
    refreshSettingsAfterSourceChange();
  }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

async function openLocalFolder(){
  try{
    await api('/api/projects/current/local-repository/open',{method:'POST',body:JSON.stringify({})});
    showToast('Opened local folder');
  }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

function openCredential(){
  openModal(`<div class="modal-content"><h2>GitHub personal access token</h2>
    <p>The token is encrypted before durable storage and is never returned by the API.</p>
    <label class="form-label" for="githubToken">Token</label><input class="field" id="githubToken" type="password" autocomplete="off">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Save token</button></div></div>`,async event=>{
    if(event.submitter?.value==='submit')try{
      await api('/api/core/integrations/github',{method:'PUT',body:JSON.stringify({token:el('githubToken').value})});
      showToast('GitHub credential saved');refreshSettingsAfterSourceChange();
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

async function deleteCredential(){
  try{await api('/api/core/integrations/github',{method:'DELETE'});showToast('GitHub credential deleted');refreshSettingsAfterSourceChange()}
  catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

async function renderEnvironments(route){
  if(!hasModule('deploy'))return renderError(new Error('Deploy module is not enabled.'));
  const project=state.context?.project;
  const environments=await api('/api/deploy/environments').catch(()=>[]);
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Deploy <span>/</span> Environments`);
  el('content').innerHTML=`
    <div class="page-header">
      <div><h1>Environments</h1><p class="description">Community allows one environment; Team unlocks multi-environment.</p></div>
      <button class="button primary" id="createEnvironment">New environment</button>
    </div>
    <section class="card">
      ${(environments||[]).length?`<table class="table"><thead><tr><th>Name</th><th>Description</th><th>Created</th><th></th></tr></thead><tbody>
        ${environments.map(env=>`<tr>
          <td><strong>${esc(env.name)}</strong></td>
          <td>${esc(env.description||'—')}</td>
          <td>${esc((env.createdAt||'').slice(0,10))}</td>
          <td><button class="button" data-route="/deployments?environmentId=${esc(env.id)}">Deployments</button></td>
        </tr>`).join('')}
      </tbody></table>`:`<div class="empty small">No environments yet.</div>`}
    </section>`;
  el('createEnvironment')?.addEventListener('click',()=>{
    openModal(`<div class="modal-content"><h2>New environment</h2>
      <label class="form-label" for="envName">Name</label><input class="field" id="envName" placeholder="production">
      <label class="form-label" for="envDescription">Description</label><input class="field" id="envDescription" placeholder="Optional">
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create</button></div></div>`,async event=>{
      if(event.submitter?.value!=='submit')return;
      try{
        await api('/api/deploy/environments',{method:'POST',body:JSON.stringify({
          name:el('envName').value,description:el('envDescription').value||null
        })});
        showToast('Environment created');
        await renderEnvironments(route);
      }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
    });
  });
}

async function renderDeployments(route){
  if(!hasModule('deploy'))return renderError(new Error('Deploy module is not enabled.'));
  const project=state.context?.project;
  const params=new URLSearchParams(route.includes('?')?route.split('?')[1]:'');
  const environmentId=params.get('environmentId');
  const [environments,deployments]=await Promise.all([
    api('/api/deploy/environments').catch(()=>[]),
    api(`/api/deploy/deployments${environmentId?`?environmentId=${encodeURIComponent(environmentId)}`:''}`).catch(()=>[])
  ]);
  const envName=id=>environments.find(e=>e.id===id)?.name||shortId(id);
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Deploy <span>/</span> Deployments`);
  el('content').innerHTML=`
    <div class="page-header">
      <div><h1>Deployments</h1><p class="description">Deploy a version to an environment and roll back when needed.</p></div>
      <button class="button primary" id="createDeployment">New deployment</button>
    </div>
    <section class="card">
      ${(deployments||[]).length?`<table class="table"><thead><tr><th>Version</th><th>Environment</th><th>Status</th><th>Triggered by</th><th>When</th><th></th></tr></thead><tbody>
        ${deployments.map(d=>`<tr>
          <td><strong>${esc(d.version)}</strong>${d.rollbackOfId?` <span class="muted">(rollback)</span>`:''}</td>
          <td>${esc(envName(d.environmentId))}</td>
          <td><span class="${statusClass(d.status)}">${esc(d.status)}</span></td>
          <td>${esc(d.triggeredBy||'—')}</td>
          <td>${esc((d.createdAt||'').slice(0,16).replace('T',' '))}</td>
          <td>${d.status!=='RolledBack'?`<button class="button" data-rollback="${esc(d.id)}">Rollback</button>`:''}</td>
        </tr>`).join('')}
      </tbody></table>`:`<div class="empty small">No deployments yet.</div>`}
    </section>`;
  el('createDeployment')?.addEventListener('click',()=>{
    if(!(environments||[]).length){showToast('Create an environment first',true);return}
    openModal(`<div class="modal-content"><h2>New deployment</h2>
      <label class="form-label" for="depEnv">Environment</label>
      <select class="field" id="depEnv">${environments.map(e=>`<option value="${esc(e.id)}" ${e.id===environmentId?'selected':''}>${esc(e.name)}</option>`).join('')}</select>
      <label class="form-label" for="depVersion">Version</label><input class="field" id="depVersion" placeholder="1.0.0">
      <label class="form-label" for="depNotes">Notes</label><input class="field" id="depNotes" placeholder="Optional">
      <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Deploy</button></div></div>`,async event=>{
      if(event.submitter?.value!=='submit')return;
      try{
        await api('/api/deploy/deployments',{method:'POST',body:JSON.stringify({
          environmentId:el('depEnv').value,version:el('depVersion').value,notes:el('depNotes').value||null
        })});
        showToast('Deployment created');
        await renderDeployments(route);
      }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
    });
  });
  document.querySelectorAll('[data-rollback]').forEach(btn=>btn.addEventListener('click',async()=>{
    try{
      await api(`/api/deploy/deployments/${btn.getAttribute('data-rollback')}/rollback`,{method:'POST',body:'{}'});
      showToast('Rollback created');
      await renderDeployments(route);
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  }));
}

window.addEventListener('hashchange',()=>{
  if(hashNavigating)return;
  navigate(location.hash.slice(1)||'/overview',false);
});
boot();
