const state={token:null,modules:[],context:null,connections:[],changes:[],change:null,local:null,projects:[],setup:null,me:null,tab:'overview',route:'/overview',expandedFiles:{},selectedJobId:null,runPoll:null,setupStep:0,peopleTab:'members',changesTab:'open'};
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
      if(state.token && state.setup.bootstrapEnabled!==false){
        // May be a bootstrap token — refresh status with it.
        try{state.setup=await api('/api/setup/status')}catch{state.token=null;localStorage.removeItem(TOKEN_KEY)}
      }
      return renderSetup();
    }
    state.token=localStorage.getItem(TOKEN_KEY);
    if(!state.token){
      try{
        const login=await api('/api/auth/login',{method:'POST',body:JSON.stringify({email:'maya@northstar.dev',password:'demo'})});
        state.token=login.token;localStorage.setItem(TOKEN_KEY,state.token);
      }catch{
        el('appSidebar').style.display='none';
        document.querySelector('.app-shell')?.classList.add('setup-mode');
        el('app').setAttribute('aria-busy','false');
        return renderLogin();
      }
    }
    state.setup=await api('/api/setup/status');
    if(state.setup.initialised && !state.setup.hasProjects){
      el('appSidebar').style.display='none';
      document.querySelector('.app-shell')?.classList.add('setup-mode');
      el('app').setAttribute('aria-busy','false');
      return renderSetup();
    }
    await loadWorkspace();
  }catch(error){
    if(error.status===401){localStorage.removeItem(TOKEN_KEY);state.token=null;return renderLogin()}
    renderError(error);
  }
}

async function loadWorkspace(){
  el('appSidebar').style.display='';
  document.querySelector('.app-shell')?.classList.remove('setup-mode');
  const [platform,context,connections,local,projects,me]=await Promise.all([
    api('/api/platform/modules'),api('/api/core/context'),api('/api/source/repositories'),
    api('/api/projects/current/local-repository'),api('/api/projects'),api('/api/users/me')
  ]);
  state.modules=platform.modules;state.context=context;state.connections=connections;state.local=local;state.projects=projects;state.me=me;
  if(hasModule('review'))state.changes=await api('/api/review/changes');
  paintShell();renderNavigation();bindShell();
  const route=location.hash.slice(1)||'/overview';
  await navigate(route,false);
  el('app').setAttribute('aria-busy','false');
  setInterval(refreshActiveChange,60000);
}

function paintShell(){
  const org=state.context?.organisation;const project=state.context?.project;
  const profile=state.me?.profile;const user=state.me?.user;
  el('orgLabel').textContent=org?.name||'Organisation';
  el('projectLabel').textContent=project?.name||'Project';
  el('projectAvatar').textContent=(project?.name||'P')[0].toUpperCase();
  el('userName').textContent=profile?.displayName||user?.username||'User';
  el('userHandle').textContent=`@${user?.username||'user'}`;
  el('userAvatar').textContent=initials(profile?.displayName||user?.username||'?');
  const chip=el('topProjectChip');
  if(chip)chip.textContent=project?.name||'Project';
  document.title=`ForgeDeck · ${project?.name||org?.name||'Workspace'}`;
}

function hasModule(id){return state.modules.some(module=>module.id===id)}
function source(){return state.connections[0]}
function actor(){return state.context?.user?.name||state.me?.profile?.displayName||'User'}
function crumbs(value){el('breadcrumbs').innerHTML=value}
function projectCrumb(...parts){
  const name=esc(state.context?.project?.name||'Project');
  return [name,...parts].join(' <span>/</span> ');
}
function statusClass(value){return String(value).toLowerCase().replace(/\s+/g,'-')}
function showToast(message,bad=false){const toast=el('toast');toast.textContent=message;toast.classList.toggle('error',bad);toast.classList.add('show');setTimeout(()=>toast.classList.remove('show'),2800)}
function renderError(error){el('content').innerHTML=`<div class="empty"><h2>${esc(error.kind||'Workspace unavailable')}</h2><p>${esc(error.message)}</p><button class="button" data-route="/settings">Open settings</button></div>`}
function initials(name){return String(name||'?').split(/\s+/).map(part=>part[0]).join('').slice(0,2).toUpperCase()}

function renderLogin(){
  el('appSidebar').style.display='none';
  document.querySelector('.app-shell')?.classList.add('setup-mode');
  crumbs('Sign in');
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <h1>Sign in</h1>
    <p class="description">Access ${esc(state.setup?.organisationName||'your organisation')}.</p>
    <label class="form-label">Email</label><input class="field" id="loginEmail" type="email" value="maya@northstar.dev">
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
  if(!s?.hasOwner) return 'owner';
  if(!s?.hasProjects) return 'project';
  if(!s?.hasRepositories) return 'repositories';
  const modulePending=(s.moduleSteps||[]).some(step=>step.isAvailable&&!step.isComplete);
  if(modulePending) return 'modules';
  return 'finish';
}

function renderSetup(){
  const step=currentSetupStepId();
  if(step==='bootstrap') return renderBootstrapLogin();
  if(step==='organisation') return renderSetupOrganisation();
  if(step==='licence') return renderSetupLicence();
  if(step==='owner') return renderSetupOwner();
  if(step==='project') return renderSetupProject();
  if(step==='repositories') return renderSetupRepositories();
  if(step==='modules') return renderSetupModules();
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
        <h2>Commercial Licence</h2>
        <p>Unlock licensed module capabilities purchased for this installation.</p>
        <label class="form-label">Licence Key / JSON</label>
        <textarea class="field" id="setupLicencePayload" rows="5" placeholder="Paste signed licence JSON"></textarea>
        <div class="modal-actions" style="margin-top:12px">
          <button class="button" id="setupValidateLicence">Validate Licence</button>
        </div>
      </article>
    </div>
  `);
  el('setupUseCommunity').onclick=async()=>{
    try{
      await api('/api/setup/licence/community',{method:'POST',body:'{}'});
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
  el('setupValidateLicence').onclick=async()=>{
    try{
      await api('/api/setup/licence/commercial',{method:'POST',body:JSON.stringify({payload:el('setupLicencePayload').value})});
      await refreshSetup();
      showToast('Commercial licence active');
      renderSetup();
    }catch(error){showToast(error.message,true)}
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

function renderSetupProject(){
  el('content').innerHTML=setupShell(`<h1>Create your first project</h1>
    <p class="description">Projects group repositories, review, pipelines, and deployments.</p>`, `
    <label class="form-label">Project Name</label><input class="field" id="setupProjectName" value="Platform">
    <label class="form-label">Slug</label><input class="field" id="setupProjectSlug" value="platform">
    <label class="form-label">Description</label><input class="field" id="setupProjectDesc" value="Modular software delivery platform">
    <p class="form-label">Repository Structure</p>
    <label class="choice-row"><input type="radio" name="repoMode" value="SingleRepository" checked> Single Repository</label>
    <label class="choice-row"><input type="radio" name="repoMode" value="MultiRepository"> Multiple Repositories</label>
    <p class="form-label" style="margin-top:14px">Visibility</p>
    <label class="choice-row"><input type="radio" name="visibility" value="Private" checked> Private</label>
    <label class="choice-row"><input type="radio" name="visibility" value="Organisation"> Organisation</label>
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupSkipProject">Skip for now</button>
      <button class="button primary" id="setupCreateProject">Create Project</button>
    </div>
  `);
  el('setupSkipProject').onclick=()=>finishSetup();
  el('setupCreateProject').onclick=async()=>{
    try{
      const mode=document.querySelector('input[name="repoMode"]:checked')?.value||'SingleRepository';
      const visibility=document.querySelector('input[name="visibility"]:checked')?.value||'Private';
      await api('/api/projects',{method:'POST',body:JSON.stringify({
        name:el('setupProjectName').value,
        slug:el('setupProjectSlug').value,
        description:el('setupProjectDesc').value,
        visibility,
        repositoryMode:mode
      })});
      showToast('Project created');
      await refreshSetup();
      renderSetup();
    }catch(error){showToast(error.message,true)}
  };
}

function renderSetupRepositories(){
  el('content').innerHTML=setupShell(`<h1>Connect your repository</h1>
    <p class="description">You can connect a source repository now or skip and finish later in Project settings.</p>`, `
    <p class="description">Use Project settings after setup to connect GitHub or a local working copy.</p>
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupSkipRepos">Skip for now</button>
      <button class="button primary" id="setupReposContinue">Continue</button>
    </div>
  `);
  const next=async()=>{await refreshSetup();renderSetupModules();};
  el('setupSkipRepos').onclick=next;
  el('setupReposContinue').onclick=next;
}

function renderSetupModules(){
  const steps=state.setup?.moduleSteps||[];
  const rows=steps.filter(s=>s.isAvailable).map(s=>`
    <div class="setup-module-row">
      <div><strong>${esc(s.title)}</strong><p class="description">${s.isComplete?'Configured':'Optional module setup available after you open the workspace.'}</p></div>
      <span class="pill">${s.isComplete?'Done':'Pending'}</span>
    </div>`).join('')||'<p class="description">No module setup steps required right now.</p>';
  el('content').innerHTML=setupShell(`<h1>Enabled module setup</h1>
    <p class="description">Installed modules can contribute optional configuration. Licensed capabilities determine what appears here.</p>`, `
    ${rows}
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupSkipModules">Skip</button>
      <button class="button primary" id="setupModulesContinue">Continue</button>
    </div>
  `);
  const go=()=>renderSetupFinish();
  el('setupSkipModules').onclick=go;
  el('setupModulesContinue').onclick=go;
}

function renderSetupFinish(){
  const s=state.setup||{};
  el('content').innerHTML=setupShell(`<h1>Setup Complete</h1>
    <p class="description">Your organisation is ready.</p>`, `
    <div class="setup-summary">
      <div><span>Organisation</span><strong>${esc(s.organisationName||'—')}</strong></div>
      <div><span>Licence</span><strong>${esc(s.licenceMode||'Community')}</strong></div>
      <div><span>Project</span><strong>${s.hasProjects?'Created':'Skipped'}</strong></div>
      <div><span>Repositories</span><strong>${s.hasRepositories?'Connected':'Not yet'}</strong></div>
    </div>
    <div class="modal-actions" style="margin-top:22px"><button class="button primary" id="setupOpenWorkspace">Open workspace</button></div>
  `);
  el('setupOpenWorkspace').onclick=async()=>{
    try{await api('/api/setup/complete',{method:'POST',body:'{}'})}catch{}
    await finishSetup();
  };
}

async function finishSetup(){
  await loadWorkspace();
  showToast('You are ready');
  navigate('/overview');
}

function openStatuses(){return state.changes.filter(change=>!['Merged','Closed'].includes(change.status))}
function hasOpenChangeForBranch(branch){return openStatuses().some(change=>change.sourceBranch===branch)}

let hashNavigating=false;

function normalizeRoute(route){
  let value=String(route??'').trim();
  if(value.startsWith('#'))value=value.slice(1);
  if(!value)return '/overview';
  if(!value.startsWith('/'))value=`/${value}`;
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
    item.classList.toggle('active',routeMatchesNav(route,item.dataset.route));
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
  const openSwitcher=()=>openModal(`<div class="modal-content"><h2>Quick switcher</h2><p>Jump to source, review, or settings.</p><div class="modal-actions"><button class="button" value="cancel">Close</button><button class="button primary" value="changes">Open changes</button></div></div>`,e=>{if(e.submitter?.value==='changes')navigate('/changes')});
  el('commandButton').onclick=openSwitcher;
  el('globalSearch')?.addEventListener('click',openSwitcher);
  el('globalSearch')?.addEventListener('focus',openSwitcher);
  el('topProjectChip')?.addEventListener('click',openProjectSwitcher);
  el('contextButton').onclick=openProjectSwitcher;
  el('userMenu').onclick=openUserMenu;
}

function openProjectSwitcher(){
  const rows=(state.projects||[]).map(p=>`<button class="button" style="width:100%;justify-content:flex-start;margin-bottom:6px" value="project:${esc(p.id)}">${esc(p.name)} <small style="color:var(--muted);margin-left:auto">/${esc(p.slug)}</small></button>`).join('')||'<p class="description">No projects yet.</p>';
  openModal(`<div class="modal-content"><h2>Projects</h2><p>Switch the active working context.</p>
    <div style="margin-top:14px">${rows}</div>
    <div class="modal-actions"><button class="button" value="cancel">Close</button><button class="button primary" value="new">New project</button></div></div>`,async e=>{
    const value=e.submitter?.value;
    if(value==='new')return openCreateProject();
    if(value?.startsWith('project:')){
      const id=value.slice(8);
      try{
        await api('/api/core/context/project',{method:'POST',body:JSON.stringify({projectId:id})});
        await loadWorkspace();
        showToast('Project switched');
      }catch(error){showToast(error.message,true)}
    }
  });
}

function openCreateProject(){
  openModal(`<div class="modal-content"><h2>New project</h2>
    <label class="form-label">Name</label><input class="field" id="newProjectName" value="New Project">
    <label class="form-label">Slug</label><input class="field" id="newProjectSlug" value="new-project">
    <label class="form-label">Description</label><input class="field" id="newProjectDesc">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      const project=await api('/api/projects',{method:'POST',body:JSON.stringify({
        name:el('newProjectName').value,slug:el('newProjectSlug').value,description:el('newProjectDesc').value||null,visibility:'Private'
      })});
      await api('/api/core/context/project',{method:'POST',body:JSON.stringify({projectId:project.id})});
      await loadWorkspace();showToast('Project created');
    }catch(error){showToast(error.message,true)}
  });
}

function openUserMenu(){
  openModal(`<div class="modal-content"><h2>${esc(state.me?.profile?.displayName||'User')}</h2>
    <p>@${esc(state.me?.user?.username||'user')}</p>
    <div class="modal-actions">
      <button class="button" value="cancel">Close</button>
      <button class="button" value="people">People</button>
      <button class="button danger" value="signout">Sign out</button>
    </div></div>`,async e=>{
    if(e.submitter?.value==='people')navigate('/people');
    if(e.submitter?.value==='signout'){
      try{await api('/api/auth/logout',{method:'POST'})}catch{}
      localStorage.removeItem(TOKEN_KEY);state.token=null;renderLogin();
    }
  });
}

function renderNavigation(){
  const openCount=openStatuses().length;
  let html='<p class="nav-group">Project</p><button type="button" class="nav-item" data-route="/overview"><span class="nav-icon">⌂</span>Overview</button>';
  html+='<p class="nav-group">Code</p><button type="button" class="nav-item" data-route="/files"><span class="nav-icon">▱</span>Repos</button><button type="button" class="nav-item" data-route="/commits"><span class="nav-icon">◉</span>Commits</button><button type="button" class="nav-item" data-route="/source-branches"><span class="nav-icon">⑂</span>Branches</button>';
  const groups={};state.modules.flatMap(module=>module.navigation||[]).sort((a,b)=>a.order-b.order).forEach(item=>(groups[item.group]??=[]).push(item));
  Object.entries(groups).forEach(([group,items])=>html+=`<p class="nav-group">${esc(group)}</p>${items.map(item=>{
    const icon=item.id==='changes'?'⑂':item.id==='pipelines'||item.id==='runs'?'≋':item.id==='runners'?'◉':'≋';
    const badge=item.id==='changes'&&openCount?`<span class="nav-badge">${openCount}</span>`:'';
    return `<button type="button" class="nav-item" data-route="${esc(item.route)}"><span class="nav-icon">${icon}</span>${esc(item.label)}${badge}</button>`;
  }).join('')}`);
  html+='<p class="nav-group">Settings</p><button type="button" class="nav-item" data-route="/settings"><span class="nav-icon">⚙</span>Project settings</button>';
  html+='<button type="button" class="nav-item" data-route="/licensing"><span class="nav-icon">▣</span>Licensing</button>';
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
  setActiveNav(route);
  try{
    if(route==='/overview')return await renderOverview();
    if(route==='/changes')return renderChanges();
    if(route==='/queue')return renderQueue();
    if(route.startsWith('/changes/')){const id=route.split('/')[2];state.change=await api(`/api/review/changes/${id}`);state.expandedFiles={};return renderChange()}
    if(route.startsWith('/files'))return await renderFiles(route);
    if(route.startsWith('/commits/'))return await renderCommitDetail(route.split('/')[2]);
    if(route==='/commits')return await renderCommits();
    if(route==='/source-branches')return await renderBranches();
    if(route==='/pipelines'||route.startsWith('/pipelines/'))return await renderPipelines(route);
    if(route==='/runs')return await renderRuns();
    {
      const jobMatch=route.match(/^\/runs\/([^/]+)\/jobs\/([^/]+)(?:\/logs)?\/?$/);
      if(jobMatch)return await renderJobPage(jobMatch[1],jobMatch[2]);
    }
    if(route.startsWith('/runs/'))return await renderRunDetail(route.split('/')[2]);
    if(route==='/runners')return await renderRunners();
    if(route==='/settings')return await renderSettings();
    if(route==='/licensing')return await renderLicensing();
    if(route==='/people')return await renderPeople();
    if(route.startsWith('/invite/'))return await renderInviteAccept(decodeURIComponent(route.slice('/invite/'.length)));
    if(route==='/audit')return await renderAudit();
    if(route==='/modules')return await renderModules();
    return await renderOverview();
  }catch(error){renderError(error)}
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

async function renderOverview(){
  const project=state.context?.project;const org=state.context?.organisation;
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')}`);
  const open=openStatuses();
  const waiting=open.filter(change=>change.reviewers.some(review=>review.name===actor()&&review.status==='Requested'));
  const approved=open.filter(change=>change.status==='Approved');
  const merged=state.changes.filter(change=>change.status==='Merged').slice().sort((a,b)=>new Date(b.mergedAt||b.updatedAt)-new Date(a.mergedAt||a.updatedAt)).slice(0,5);

  let runs=[],members=[],audit=[];
  try{
    const jobs=[];
    if(hasModule('pipelines'))jobs.push(api('/api/pipelines/runs').then(r=>runs=r||[]).catch(()=>[]));
    jobs.push(api('/api/organisation/members').then(r=>members=r||[]).catch(()=>[]));
    jobs.push(api('/api/core/audit').then(r=>audit=r||[]).catch(()=>[]));
    await Promise.all(jobs);
  }catch{/* overview widgets degrade gracefully */}

  const terminal=runs.filter(r=>['Succeeded','Failed','Cancelled','PartiallySucceeded'].includes(r.status));
  const succeeded=terminal.filter(r=>r.status==='Succeeded'||r.status==='PartiallySucceeded').length;
  const successRate=terminal.length?Math.round((succeeded/terminal.length)*100):null;
  const local=state.local?.status;
  const localHealthy=state.local?.associated?!local||local.isClean!==false:null;
  const tags=['delivery',source()?.repositoryId?.provider||'git',hasModule('pipelines')?'pipelines':'',hasModule('review')?'review':'','.net'].filter(Boolean);
  const desc=project?.description||`${org?.name||'Organisation'} delivery workspace with GitHub-backed review and pipelines.`;
  const recentRuns=runs.slice(0,5);
  const activity=buildOverviewActivity(open,merged,runs,audit);
  const tech=['.NET','React','GitHub','Docker','SQLite','Azure'].slice(0,hasModule('pipelines')?6:4);

  el('content').innerHTML=`
  <section class="project-hero">
    <div class="project-hero-top">
      <div class="project-identity">
        <div class="project-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><path d="M4 17L12 5l8 12H4z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="M8 17h8" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </div>
        <div>
          <h1>${esc(project?.name||'Project')} <span class="pill visibility-pill">${esc(project?.visibility||'Organisation')}</span></h1>
          <p class="project-desc">${esc(desc)}</p>
          <div class="tag-row">${tags.map(t=>`<span class="tag">${esc(t)}</span>`).join('')}</div>
        </div>
      </div>
      <div class="header-actions">
        <button class="button" data-route="/files">Code</button>
        ${hasModule('pipelines')?'<button class="button" data-route="/pipelines">Run pipeline</button>':''}
        ${hasModule('review')?'<button class="button primary" data-route="/changes">＋ Create change</button>':''}
      </div>
    </div>
  </section>

  <div class="metric-row">
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Pipeline success rate</p>
          <p class="metric-value">${successRate==null?'—':`${successRate}%`}</p>
          <p class="metric-trend ${successRate==null?'flat':successRate>=80?'up':'down'}">${terminal.length?`${succeeded}/${terminal.length} recent runs`:'No runs yet'}</p>
        </div>
        <div class="metric-ring" style="--pct:${successRate??0}"></div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Active pipeline jobs</p>
          <p class="metric-value">${runs.filter(r=>['Queued','Running','Waiting'].includes(r.status)).length}</p>
          <p class="metric-trend flat">${runs.length} total runs</p>
        </div>
        <div class="metric-icon green">↗</div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Open pull requests</p>
          <p class="metric-value">${open.length}</p>
          <p class="metric-trend ${waiting.length?'down':'flat'}">${waiting.length?`${waiting.length} waiting on you`:`${approved.length} approved`}</p>
        </div>
        <div class="metric-icon">⑂</div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Lead time (merged)</p>
          <p class="metric-value">${merged.length||'—'}</p>
          <p class="metric-trend up">${merged.length?'Recently merged':'No merges yet'}</p>
        </div>
        <div class="metric-icon slate">◷</div>
      </div>
    </article>
    <article class="metric-card">
      <div class="metric-head">
        <div>
          <p class="metric-label">Environments</p>
          <p class="metric-value">${state.local?.associated?1:0}<span style="font-size:.9rem;font-weight:600;color:var(--muted)"> / local</span></p>
          <div class="env-pills">
            <span class="env-pill ${localHealthy===false?'warn':'ok'}">${state.local?.associated?(localHealthy===false?'Dirty tree':'Healthy'):'Not linked'}</span>
          </div>
        </div>
        <div class="metric-icon amber">◎</div>
      </div>
    </article>
  </div>

  <div class="dash-grid">
    <div class="dash-col">
      <div class="card"><div class="card-header"><h2>About this project</h2></div><div class="card-body">
        <p class="description">${esc(desc)}</p>
        <div class="meta-list" style="margin-top:14px">
          <div class="meta-row"><span>Owner</span><strong>${esc(org?.name||'—')}</strong></div>
          <div class="meta-row"><span>Organisation</span><strong>${esc(org?.name||'—')}</strong></div>
          <div class="meta-row"><span>Default branch</span><strong>${esc(source()?.defaultBranch||local?.currentBranch||'main')}</strong></div>
          <div class="meta-row"><span>Repository</span><strong>${source()?`${esc(source().repositoryId.owner)}/${esc(source().repositoryId.name)}`:'Not connected'}</strong></div>
          <div class="meta-row"><span>Local path</span><strong>${esc(local?.root||local?.path||'—')}</strong></div>
        </div>
      </div></div>
      <div class="card"><div class="card-header"><h2>Environments</h2><button class="button text" data-route="/settings">Manage</button></div><div class="card-body">
        ${state.local?.associated?`<div class="env-row"><span class="env-dot ${localHealthy===false?'warn':'ok'}"></span><div><strong>Local working copy</strong><small>${esc(local?.currentBranch||'—')} · ${esc((local?.headSha||'').slice(0,7)||'no HEAD')}</small></div><span class="pill">${local?.isClean?'Clean':'Dirty'}</span></div>`:`<div class="empty small">Connect a local repository in Settings.</div>`}
        ${source()?`<div class="env-row"><span class="env-dot ok"></span><div><strong>GitHub source</strong><small>${esc(source().url||'')}</small></div><span class="pill">Connected</span></div>`:''}
      </div></div>
    </div>

    <div class="dash-col">
      <div class="card"><div class="card-header"><h2>Recent activity</h2><button class="button text" data-route="/audit">View all</button></div><div class="card-body">
        ${activity.length?activity.map(item=>`<div class="activity-item"><span class="avatar">${esc(item.initials)}</span><div><p><strong>${esc(item.actor)}</strong> ${esc(item.detail)}</p></div><small>${esc(item.when)}</small></div>`).join(''):'<div class="empty small">Activity will appear as you review and run pipelines.</div>'}
      </div></div>
      <div class="card"><div class="card-header"><h2>Repositories &amp; services</h2><button class="button text" data-route="/files">Browse</button></div><div class="card-body">
        ${(state.connections||[]).length?(state.connections||[]).map(c=>`<div class="repo-row"><div><strong>${esc(c.repositoryId.owner)}/${esc(c.repositoryId.name)}</strong><small><span class="lang-dot" style="background:#3178c6"></span>${esc(c.repositoryId.provider)} · default ${esc(c.defaultBranch)}</small></div><span class="pill">Source</span></div>`).join(''):'<div class="empty small">No repository connected.</div>'}
        ${state.local?.associated?`<div class="repo-row"><div><strong>local working copy</strong><small><span class="lang-dot" style="background:#512bd4"></span>.NET · ${esc(local?.currentBranch||'—')}</small></div><span class="pill">Local</span></div>`:''}
      </div></div>
    </div>

    <div class="dash-col">
      <div class="card"><div class="card-header"><h2>Latest pipeline runs</h2><button class="button text" data-route="/runs">View all</button></div><div class="card-body">
        ${recentRuns.length?recentRuns.map(run=>`<div class="run-mini" data-route="/runs/${esc(run.id)}">
          <span class="run-status ${statusClass(run.status)}">${checkIcon(run.status)}</span>
          <div style="flex:1;min-width:0"><strong>${esc(run.definitionName||'Pipeline')}</strong><div><code>#${esc(shortId(run.id))}</code> · ${esc(run.ref||'—')}</div></div>
          <div style="text-align:right"><span class="status ${statusClass(run.status)}">${esc(run.status)}</span><div><small style="color:var(--muted)">${esc(durationLabel(run.startedAt,run.completedAt))}</small></div></div>
        </div>`).join(''):`<div class="empty small">${hasModule('pipelines')?'No runs yet.':'Pipelines module is not enabled.'}</div>`}
      </div></div>
      <div class="card"><div class="card-header"><h2>Team</h2><button class="button text" data-route="/people">Manage</button></div><div class="card-body">
        ${members.slice(0,5).map(m=>`<div class="team-row"><span class="avatar">${esc(initials(m.profile?.displayName||m.user?.username))}</span><div><strong>${esc(m.profile?.displayName||m.user?.username)}</strong><small>@${esc(m.user?.username)}</small></div><span class="pill">${esc(m.membership?.role||'Member')}</span></div>`).join('')||'<div class="empty small">No members loaded.</div>'}
        ${members.length>5?`<p class="description" style="margin:8px 0 0">+${members.length-5} more</p>`:''}
      </div></div>
      <div class="card"><div class="card-header"><h2>Tech stack</h2></div><div class="card-body">
        <div class="tech-grid">${tech.map(t=>`<div class="tech-chip"><span>${esc(t[0])}</span>${esc(t)}</div>`).join('')}</div>
      </div></div>
      <div class="card"><div class="card-header"><h2>Work items</h2></div><div class="card-body">
        <div class="work-row"><span>Open changes</span><span class="work-count">${open.length}</span></div>
        <div class="work-row"><span>Waiting for me</span><span class="work-count">${waiting.length}</span></div>
        <div class="work-row"><span>Approved</span><span class="work-count">${approved.length}</span></div>
        <div class="work-row"><span>Recently merged</span><span class="work-count">${merged.length}</span></div>
      </div></div>
    </div>
  </div>`;
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
  crumbs(`${esc(state.context?.organisation?.name||'Organisation')} <span>/</span> People`);
  const tabs=`<div class="filterbar">
    <button class="button ${tab==='members'?'primary':''}" id="peopleMembers">Members</button>
    <button class="button ${tab==='teams'?'primary':''}" id="peopleTeams">Teams</button>
    <button class="button ${tab==='invitations'?'primary':''}" id="peopleInvites">Invitations</button>
  </div>`;
  if(tab==='teams')return renderPeopleTeams(tabs);
  if(tab==='invitations')return renderPeopleInvitations(tabs);
  const members=await api('/api/organisation/members');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>People</h1><p>Members of this organisation.</p></div>
    <button class="button primary" id="inviteMember">Invite</button></div>${tabs}
    <div class="card">${members.map(m=>`<div class="module-card">
      <span class="module-logo">${esc(initials(m.profile?.displayName||m.user?.username))}</span>
      <div><h3>${esc(m.profile?.displayName||m.user?.username)}</h3><p>@${esc(m.user?.username)} · ${esc(m.user?.email)} · ${esc(m.membership?.role)} · ${esc(m.membership?.status)}</p></div>
      <span class="module-state">${esc(m.membership?.role)}</span>
    </div>`).join('')||'<div class="empty">No members.</div>'}</div>`;
  wirePeopleTabs();
  el('inviteMember').onclick=()=>openModal(`<div class="modal-content"><h2>Invite member</h2>
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
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Teams</h1><p>Reusable groups for project access and policy.</p></div>
    <button class="button primary" id="createTeam">Create team</button></div>${tabs}
    <div class="card">${teams.map(t=>`<div class="module-card">
      <div><h3>${esc(t.name)}</h3><p>/${esc(t.slug)}${t.description?` · ${esc(t.description)}`:''}</p></div>
      <button class="button" data-team="${esc(t.id)}">Open</button>
    </div>`).join('')||'<div class="empty">No teams yet.</div>'}</div>`;
  wirePeopleTabs();
  el('createTeam').onclick=()=>openModal(`<div class="modal-content"><h2>Create team</h2>
    <label class="form-label">Name</label><input class="field" id="teamName" value="Backend">
    <label class="form-label">Slug</label><input class="field" id="teamSlug" value="backend">
    <label class="form-label">Description</label><input class="field" id="teamDesc">
    <div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="submit">Create</button></div></div>`,async e=>{
    if(e.submitter?.value!=='submit')return;
    try{
      await api('/api/teams',{method:'POST',body:JSON.stringify({name:el('teamName').value,slug:el('teamSlug').value,description:el('teamDesc').value||null})});
      showToast('Team created');state.peopleTab='teams';renderPeople();
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

async function renderPeopleInvitations(tabs){
  const invitations=await api('/api/organisation/invitations');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Invitations</h1><p>Pending and accepted invitations.</p></div>
    <button class="button primary" id="inviteMember">Invite</button></div>${tabs}
    <div class="card">${invitations.map(i=>`<div class="module-card">
      <div><h3>${esc(i.email)}</h3><p>${esc(i.role)} · expires ${esc(new Date(i.expiresAt).toLocaleString())}${i.acceptedAt?' · accepted':''}</p></div>
      <span class="module-state">${i.acceptedAt?'Accepted':'Pending'}</span>
    </div>`).join('')||'<div class="empty">No invitations.</div>'}</div>`;
  wirePeopleTabs();
  el('inviteMember').onclick=()=>{state.peopleTab='members';renderPeople().then(()=>el('inviteMember')?.click())};
}

function wirePeopleTabs(){
  el('peopleMembers').onclick=()=>{state.peopleTab='members';renderPeople()};
  el('peopleTeams').onclick=()=>{state.peopleTab='teams';renderPeople()};
  el('peopleInvites').onclick=()=>{state.peopleTab='invitations';renderPeople()};
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
        ${hasModule('review')?'<button class="button primary" data-route="/changes">＋ Create change</button>':''}
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
    <div class="card">${commits.map(c=>`<button class="commit-row" data-route="/commits/${esc(c.sha)}" style="width:100%;border:0;background:transparent;cursor:pointer;text-align:left">
      <code>${esc(c.sha.slice(0,7))}</code><div><strong>${esc(c.message.split('\n')[0])}</strong><small>${esc(c.author)} · ${new Date(c.authoredAt).toLocaleString()}</small></div>
    </button>`).join('')}</div>`;
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
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Branches</h1><p>Create a GitHub pull request without changing your Git workflow.</p></div></div>
    <div class="card">${branches.map(branch=>{
      const skip=branch.isDefault||hasOpenChangeForBranch(branch.name);
      return `<div class="branch-row"><div><strong>${esc(branch.name)} ${branch.isDefault?'<span class="pill">default</span>':''}${hasOpenChangeForBranch(branch.name)&&!branch.isDefault?'<span class="pill">open change</span>':''}</strong>
        <small>${esc(branch.author)} · ${new Date(branch.updatedAt).toLocaleString()} · ${esc(branch.headSha.slice(0,7))}${branch.headMessage?` · ${esc(branch.headMessage)}`:''}</small></div>
        ${skip?'':`<button class="button" data-create-change="${esc(branch.name)}">Create Change</button>`}
      </div>`;
    }).join('')}</div>`;
  document.querySelectorAll('[data-create-change]').forEach(button=>button.onclick=()=>openCreateChange(button.dataset.createChange));
}

function renderNoSource(){el('content').innerHTML='<div class="empty"><h2>No source repository connected</h2><p>Connect the project repository in Settings.</p><button class="button primary" data-route="/settings">Open settings</button></div>'}

async function renderSettings(){
  const integration=await api('/api/core/integrations/github');
  crumbs(projectCrumb('Settings <span>/</span> Source repositories'));
  const local=state.local;
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Project settings</h1><p>Configure the local working copy and GitHub as the authoritative source provider.</p></div></div>
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
  </div></div></aside></div>`;
  const connectLocal=el('connectLocal');if(connectLocal)connectLocal.onclick=openConnectLocal;
  const disconnectLocal=el('disconnectLocal');if(disconnectLocal)disconnectLocal.onclick=disconnectLocalRepository;
  const settingsOpenFolder=el('settingsOpenFolder');if(settingsOpenFolder)settingsOpenFolder.onclick=openLocalFolder;
  el('connectRepository').onclick=openConnect;
  el('credentialButton').onclick=openCredential;
  if(el('deleteCredential'))el('deleteCredential').onclick=deleteCredential;
}

async function renderAudit(){
  crumbs(projectCrumb('Audit'));
  const audit=await api('/api/core/audit');
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Audit log</h1><p>Security and workflow actions, separate from Change activity.</p></div></div>
    <div class="card">${audit.map(item=>`<div class="audit-row"><span class="event-icon">◎</span><div><strong>${esc(item.actor)}</strong> <code>${esc(item.action)}</code><br><small>${esc(item.module)} · ${esc(item.resource)}</small></div><small>${new Date(item.timestamp).toLocaleString()}</small></div>`).join('')||'<div class="empty">No audited actions yet.</div>'}</div>`;
}

async function renderModules(){
  crumbs(projectCrumb('Modules'));
  const platform=await api('/api/platform/modules');
  state.modules=platform.modules;
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Runtime composition</h1><p>Installed modules and the capabilities this installation is entitled to use.</p></div></div>
    <div class="card" id="modulesList">${state.modules.map(module=>`<div class="module-card" data-module-id="${esc(module.id)}" data-edition="${esc(module.edition||'Community')}">
      <span class="module-logo">${esc(module.name[0])}</span>
      <div>
        <h3>${esc(module.name)} <span class="pill module-edition">${esc(module.edition||'Community')}</span></h3>
        <p data-capabilities>${(module.capabilities||[]).map(esc).join(' · ')||'No granted capabilities'}</p>
      </div>
      <span class="module-state">● Enabled</span>
    </div>`).join('')}
    <div class="module-card" data-module-id="github-connector" data-edition="Community"><span class="module-logo">G</span><div><h3>GitHub Connector <span class="pill module-edition">Community</span></h3><p>Repository source · change source · real GitHub REST API</p></div><span class="module-state">● Available</span></div></div>`;
}

async function renderLicensing(){
  crumbs(`${esc(state.context?.organisation?.name||'Organisation')} <span>/</span> Licensing`);
  const licence=await api('/api/licensing');
  const modules=(licence.modules||[]).map(m=>`
    <tr data-licence-module="${esc(m.moduleId||m.name)}" data-edition="${esc(m.edition)}"><td>${esc(m.name)}</td><td><span class="pill">${esc(m.edition)}</span></td><td>${m.installed?'Installed':'Not installed'}</td></tr>`).join('');
  el('content').innerHTML=`
  <div class="list-page-header"><div>
    <h1>Licensing</h1>
    <p class="description">Licensing is capability and module based for this installation.</p>
  </div></div>
  <div class="card" id="licensingStatus">
    <div class="card-header"><h2>Current licence</h2><span class="pill" id="licensingStatusPill">${esc(licence.status)}</span></div>
    <div class="card-body setup-summary">
      <div><span>Type</span><strong id="licensingMode">${esc(licence.mode)}</strong></div>
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
  <div class="card" style="margin-top:16px">
    <div class="card-header"><h2>${licence.mode==='Commercial'?'Replace licence':'Add commercial licence'}</h2></div>
    <div class="card-body">
      <label class="form-label">Licence JSON</label>
      <textarea class="field" id="licensingPayload" rows="6" placeholder="Paste signed licence JSON"></textarea>
      <div class="modal-actions" style="margin-top:14px">
        <button class="button" id="licensingCommunity">Use Community</button>
        ${licence.mode==='Commercial'?`<button class="button" id="licensingRemove">Remove commercial</button>`:''}
        <button class="button primary" id="licensingInstall">Validate &amp; install</button>
      </div>
    </div>
  </div>`;
  el('licensingCommunity').onclick=async()=>{
    try{await api('/api/licensing/community',{method:'POST',body:'{}'});showToast('Community licence active');await renderLicensing()}
    catch(error){showToast(error.message,true)}
  };
  el('licensingInstall').onclick=async()=>{
    try{await api('/api/licensing/commercial',{method:'POST',body:JSON.stringify({payload:el('licensingPayload').value})});showToast('Licence installed');await renderLicensing()}
    catch(error){showToast(error.message,true)}
  };
  const remove=el('licensingRemove');
  if(remove)remove.onclick=async()=>{
    if(!confirm('Remove the commercial licence and continue with Community?'))return;
    try{await api('/api/licensing/commercial',{method:'DELETE'});showToast('Commercial licence removed');await renderLicensing()}
    catch(error){showToast(error.message,true)}
  };
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
  if(!hasModule('pipelines'))return renderError(new Error('Pipelines module is not enabled.'));
  const project=state.context?.project;
  const [definitions,runs]=await Promise.all([
    api('/api/pipelines/definitions'),
    api('/api/pipelines/runs').catch(()=>[])
  ]);
  const id=route.split('/')[2];
  const selected=id?definitions.find(d=>d.id===id):null;
  if(!state.pipelinesFilter)state.pipelinesFilter='all';
  crumbs(`Projects <span>/</span> ${esc(project?.name||'Project')} <span>/</span> Pipelines${selected?` <span>/</span> ${esc(selected.name)}`:''}`);

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
          <h1>Pipelines <span class="pill visibility-pill">Public</span></h1>
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
  el('newPipelineHint').onclick=()=>showToast('Custom pipeline authoring is coming soon');
  el('pipelineSearch').oninput=e=>{state.pipelinesSearch=e.target.value;renderPipelines(route)};
  el('pipelineSort').onchange=e=>{state.pipelinesSort=e.target.value;renderPipelines(route)};
  document.querySelectorAll('[data-pipe-filter]').forEach(btn=>btn.onclick=()=>{state.pipelinesFilter=btn.dataset.pipeFilter;renderPipelines(route)});
  document.querySelectorAll('[data-template]').forEach(btn=>btn.onclick=()=>showToast(`Template “${btn.dataset.template}” is a preview — use .NET Validation to run today`));
  document.querySelectorAll('[data-run-pipeline]').forEach(button=>button.onclick=()=>openRunPipeline(button.dataset.runPipeline));
  document.querySelectorAll('[data-edit-pipeline]').forEach(button=>button.onclick=()=>navigate(`/pipelines/${button.dataset.editPipeline}`));
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
      <button class="button compact" data-edit-pipeline="${esc(def.id)}">View</button>
      <button class="button compact" data-toggle-pipeline="${esc(def.id)}" data-enabled="${def.enabled?'1':'0'}">${def.enabled?'Disable':'Enable'}</button>
      <button class="button compact danger" data-delete-pipeline="${esc(def.id)}">Delete</button>
      <button class="button compact primary" data-run-pipeline="${esc(def.id)}" ${def.enabled?'':'disabled'}>Run</button>
    </div>
  </article>`;
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
  if(!hasModule('pipelines'))return renderError(new Error('Pipelines module is not enabled.'));
  const runs=await api('/api/pipelines/runs');
  crumbs(projectCrumb('Automation <span>/</span> Runs'));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Runs</h1><p>Recent pipeline executions.</p></div>
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
  if(!hasModule('pipelines'))return renderError(new Error('Pipelines module is not enabled.'));
  clearInterval(state.runPoll);
  const run=await api(`/api/pipelines/runs/${runId}`);
  const running=isActiveStatus(run.status)||(run.jobs||[]).some(j=>isActiveStatus(j.status));
  const failedJob=(run.jobs||[]).find(j=>j.status==='Failed');
  crumbs(projectCrumb(`Automation <span>/</span> <button class="button text" data-route="/runs">Runs</button> <span>/</span> ${esc(shortId(run.id))}`));
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
  if(!hasModule('pipelines'))return renderError(new Error('Pipelines module is not enabled.'));
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

  crumbs(projectCrumb(`Automation <span>/</span> <button class="button text" data-route="/runs">Runs</button> <span>/</span> <button class="button text" data-route="/runs/${esc(runId)}">${esc(shortId(runId))}</button> <span>/</span> ${esc(job.name)}`));
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
  if(!hasModule('pipelines'))return renderError(new Error('Pipelines module is not enabled.'));
  const runners=await api('/api/pipelines/runners');
  crumbs(projectCrumb('Automation <span>/</span> Runners'));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Runners</h1><p>Self-hosted workers connected to this control plane.</p></div>
    <div class="header-actions"><button class="button" id="refreshRunners">Refresh</button><button class="button primary" id="addRunner">＋ Add Runner</button></div></div>
    <div class="card">${runners.map(r=>`<article class="pipeline-row">
      <span class="run-status ${statusClass(r.status)}">${checkIcon(r.status)}</span>
      <div><h3>${esc(r.name)}</h3><p>${esc(r.operatingSystem||'—')} · ${(r.capabilities||[]).map(esc).join(', ')||'no caps'} · v${esc(r.version||'?')} · jobs ${esc(r.currentJobCount)}/${esc(r.concurrency)} · last seen ${r.lastHeartbeatAt?new Date(r.lastHeartbeatAt).toLocaleTimeString():'—'}</p></div>
      <div class="header-actions"><span class="status ${statusClass(r.status)}">${esc(r.status)}</span><button class="button danger" data-revoke-runner="${esc(r.id)}">Revoke</button></div>
    </article>`).join('')||'<div class="empty">No runners registered yet.</div>'}</div>
    <div class="policy-box" style="margin-top:18px">Pipeline commands execute with the runner process identity. Treat registration as privileged infrastructure.</div>`;
  el('refreshRunners').onclick=renderRunners;
  el('addRunner').onclick=openAddRunner;
  document.querySelectorAll('[data-revoke-runner]').forEach(button=>button.onclick=async()=>{
    if(!confirm('Revoke this runner? It will need a new registration token.'))return;
    try{await api(`/api/pipelines/runners/${button.dataset.revokeRunner}`,{method:'DELETE'});showToast('Runner revoked');renderRunners()}catch(error){showToast(error.message,true)}
  });
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
      state.connections=await api('/api/source/repositories');showToast('Repository connected');renderSettings();
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
      if(state.route==='/settings')renderSettings();else renderOverview();
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
    renderSettings();
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
      showToast('GitHub credential saved');renderSettings();
    }catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
  });
}

async function deleteCredential(){
  try{await api('/api/core/integrations/github',{method:'DELETE'});showToast('GitHub credential deleted');renderSettings()}
  catch(error){showToast(error.kind?`${error.kind}: ${error.message}`:error.message,true)}
}

window.addEventListener('hashchange',()=>{
  if(hashNavigating)return;
  navigate(location.hash.slice(1)||'/overview',false);
});
boot();
