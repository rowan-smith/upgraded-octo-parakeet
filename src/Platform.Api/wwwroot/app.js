const state={token:null,modules:[],context:null,connections:[],changes:[],change:null,local:null,projects:[],setup:null,me:null,tab:'overview',route:'/overview',expandedFiles:{},selectedJobId:null,runPoll:null,setupStep:0,peopleTab:'members'};
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
      return renderSetupProject();
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

function renderSetup(){
  crumbs('Setup');
  const step=state.setupStep||0;
  if(step===0){
    el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
      <p class="eyebrow"><span class="repo-mark">ForgeDeck</span></p>
      <h1>Welcome</h1>
      <p class="description">Set up your software delivery platform. This installation represents one organisation.</p>
      <div class="modal-actions"><button class="button primary" id="setupStart">Get Started</button></div>
    </div></div>`;
    el('setupStart').onclick=()=>{state.setupStep=1;renderSetup()};
    return;
  }
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <h1>Create your organisation</h1>
    <p class="description">You will become the Owner of this organisation.</p>
    <label class="form-label">Organisation name</label><input class="field" id="setupOrgName" value="Northstar Engineering">
    <label class="form-label">Description</label><input class="field" id="setupOrgDesc" placeholder="Optional">
    <hr style="border:0;border-top:1px solid var(--line);margin:22px 0">
    <h2 style="margin:0 0 8px;font-size:1.05rem">Owner account</h2>
    <label class="form-label">Display name</label><input class="field" id="setupDisplayName" value="Rowan Smith">
    <label class="form-label">Username</label><input class="field" id="setupUsername" value="rowan">
    <label class="form-label">Email</label><input class="field" id="setupEmail" type="email" value="rowan@example.com">
    <label class="form-label">Password</label><input class="field" id="setupPassword" type="password" value="password123">
    <label class="form-label">Confirm password</label><input class="field" id="setupPassword2" type="password" value="password123">
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupBack">Back</button>
      <button class="button primary" id="setupContinue">Continue</button>
    </div>
  </div></div>`;
  el('setupBack').onclick=()=>{state.setupStep=0;renderSetup()};
  el('setupContinue').onclick=async()=>{
    const password=el('setupPassword').value;
    if(password!==el('setupPassword2').value)return showToast('Passwords do not match',true);
    try{
      const result=await api('/api/setup',{method:'POST',body:JSON.stringify({
        organisationName:el('setupOrgName').value,
        organisationDescription:el('setupOrgDesc').value||null,
        displayName:el('setupDisplayName').value,
        username:el('setupUsername').value,
        email:el('setupEmail').value,
        password
      })});
      state.token=result.token;localStorage.setItem(TOKEN_KEY,state.token);
      state.setup={initialised:true,organisationName:result.organisation?.name,hasProjects:false,hasRepositories:false};
      state.setupStep=2;renderSetupProject();
    }catch(error){showToast(error.message,true)}
  };
}

function renderSetupProject(){
  crumbs('Setup <span>/</span> Project');
  el('content').innerHTML=`<div class="setup-shell"><div class="setup-card">
    <h1>Create your first project</h1>
    <p class="description">Projects group repositories, review, pipelines, and deployments.</p>
    <label class="form-label">Name</label><input class="field" id="setupProjectName" value="Platform">
    <label class="form-label">Slug</label><input class="field" id="setupProjectSlug" value="platform">
    <label class="form-label">Description</label><input class="field" id="setupProjectDesc" value="Modular software delivery platform">
    <div class="modal-actions" style="margin-top:22px">
      <button class="button" id="setupSkipProject">Skip for now</button>
      <button class="button primary" id="setupCreateProject">Create Project</button>
    </div>
  </div></div>`;
  el('setupSkipProject').onclick=()=>finishSetup();
  el('setupCreateProject').onclick=async()=>{
    try{
      await api('/api/projects',{method:'POST',body:JSON.stringify({
        name:el('setupProjectName').value,
        slug:el('setupProjectSlug').value,
        description:el('setupProjectDesc').value,
        visibility:'Private'
      })});
      showToast('Project created');
      await finishSetup();
    }catch(error){showToast(error.message,true)}
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
  el('commandButton').onclick=()=>openModal(`<div class="modal-content"><h2>Quick switcher</h2><p>Jump to source, review, or settings.</p><div class="modal-actions"><button class="button" value="cancel">Close</button><button class="button primary" value="changes">Open changes</button></div></div>`,e=>{if(e.submitter?.value==='changes')navigate('/changes')});
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
  let html='<p class="nav-group">Project</p><button type="button" class="nav-item" data-route="/overview"><span class="nav-icon">⌂</span>Overview</button>';
  html+='<p class="nav-group">Code</p><button type="button" class="nav-item" data-route="/files"><span class="nav-icon">▱</span>Files</button><button type="button" class="nav-item" data-route="/commits"><span class="nav-icon">◉</span>Commits</button><button type="button" class="nav-item" data-route="/source-branches"><span class="nav-icon">⑂</span>Branches</button>';
  const groups={};state.modules.flatMap(module=>module.navigation||[]).sort((a,b)=>a.order-b.order).forEach(item=>(groups[item.group]??=[]).push(item));
  Object.entries(groups).forEach(([group,items])=>html+=`<p class="nav-group">${esc(group)}</p>${items.map(item=>{
    const icon=item.id==='changes'?'⑂':item.id==='pipelines'||item.id==='runs'?'≋':item.id==='runners'?'◉':'≋';
    return `<button type="button" class="nav-item" data-route="${esc(item.route)}"><span class="nav-icon">${icon}</span>${esc(item.label)}</button>`;
  }).join('')}`);
  html+='<p class="nav-group">Settings</p><button type="button" class="nav-item" data-route="/settings"><span class="nav-icon">⚙</span>Project settings</button>';el('primaryNav').innerHTML=html;
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
    if(route==='/overview')return renderOverview();
    if(route==='/changes')return renderChanges();
    if(route==='/queue')return renderQueue();
    if(route.startsWith('/changes/')){const id=route.split('/')[2];state.change=await api(`/api/review/changes/${id}`);state.expandedFiles={};return renderChange()}
    if(route.startsWith('/files'))return renderFiles(route);
    if(route.startsWith('/commits/'))return renderCommitDetail(route.split('/')[2]);
    if(route==='/commits')return renderCommits();
    if(route==='/source-branches')return renderBranches();
    if(route==='/pipelines'||route.startsWith('/pipelines/'))return renderPipelines(route);
    if(route==='/runs')return renderRuns();
    {
      const jobMatch=route.match(/^\/runs\/([^/]+)\/jobs\/([^/]+)(?:\/logs)?\/?$/);
      if(jobMatch)return renderJobPage(jobMatch[1],jobMatch[2]);
    }
    if(route.startsWith('/runs/'))return renderRunDetail(route.split('/')[2]);
    if(route==='/runners')return renderRunners();
    if(route==='/settings')return renderSettings();
    if(route==='/people')return renderPeople();
    if(route.startsWith('/invite/'))return renderInviteAccept(decodeURIComponent(route.slice('/invite/'.length)));
    if(route==='/audit')return renderAudit();
    if(route==='/modules')return renderModules();
    return renderOverview();
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

function renderOverview(){
  const project=state.context?.project;const org=state.context?.organisation;
  crumbs(projectCrumb('Overview'));
  const open=openStatuses();
  const waiting=open.filter(change=>change.reviewers.some(review=>review.name===actor()&&review.status==='Requested'));
  const approved=open.filter(change=>change.status==='Approved');
  const merged=state.changes.filter(change=>change.status==='Merged').slice().sort((a,b)=>new Date(b.mergedAt||b.updatedAt)-new Date(a.mergedAt||a.updatedAt)).slice(0,5);
  el('content').innerHTML=`<div class="list-page-header"><div><h1>${esc(project?.name||'Project')}</h1><p>${esc(org?.name||'Organisation')} · local development with GitHub-backed review.</p></div>${hasModule('review')?'<button class="button primary" data-route="/changes">Open review</button>':''}</div>
  <div class="panel-grid"><div>
    <div class="card"><div class="card-header"><h2>Review activity</h2></div>
      <div class="card-body" style="display:grid;grid-template-columns:repeat(4,1fr);gap:12px">
        <div class="side-stat" style="border:0;flex-direction:column;align-items:flex-start;gap:4px"><span>Open</span><strong>${open.length}</strong></div>
        <div class="side-stat" style="border:0;flex-direction:column;align-items:flex-start;gap:4px"><span>Waiting for me</span><strong>${waiting.length}</strong></div>
        <div class="side-stat" style="border:0;flex-direction:column;align-items:flex-start;gap:4px"><span>Approved</span><strong>${approved.length}</strong></div>
        <div class="side-stat" style="border:0;flex-direction:column;align-items:flex-start;gap:4px"><span>Recently merged</span><strong>${merged.length}</strong></div>
      </div>
      ${open.length?open.slice(0,5).map(changeRow).join(''):'<div class="empty">No open Changes. Import or create one when a branch is ready.</div>'}
    </div>
    ${merged.length?`<div class="card" style="margin-top:18px"><div class="card-header"><h2>Recently merged</h2></div>${merged.map(changeRow).join('')}</div>`:''}
  </div>
  <aside>${localStatusCard()}
    <div class="card" style="margin-top:18px"><div class="card-header"><h2>Project</h2></div><div class="card-body">
      <div class="side-stat"><span>Organisation</span><strong>${esc(org?.name||'—')}</strong></div>
      <div class="side-stat"><span>Repository</span><strong>${source()?`${esc(source().repositoryId.owner)}/${esc(source().repositoryId.name)}`:'Not connected'}</strong></div>
      <div class="side-stat"><span>Open changes</span><strong>${open.length}</strong></div>
      <div class="side-stat"><span>Waiting for me</span><strong>${waiting.length}</strong></div>
      <div class="policy-box">GitHub owns refs and merge state. ForgeDeck owns review discussion and policy.</div>
    </div></div>
  </aside></div>`;
  const connect=el('overviewConnectLocal');if(connect)connect.onclick=openConnectLocal;
  const openFolder=el('overviewOpenFolder');if(openFolder)openFolder.onclick=openLocalFolder;
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

function changeRow(change){return `<article class="change-row" data-route="/changes/${change.id}"><span class="number">#${esc(change.externalNumber||change.externalId)}</span><div><h3>${esc(change.title)}</h3><p>${esc(change.author)} · ${esc(change.sourceBranch)} → ${esc(change.targetBranch)}</p></div><span class="status ${statusClass(change.status)}">${esc(change.status)}</span></article>`}

function renderChanges(){
  crumbs(projectCrumb('Review <span>/</span> Changes'));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Changes</h1><p>Pull requests represented as provider-neutral Changes.</p></div>
    <div class="header-actions"><button class="button" id="refreshChanges">Refresh</button><button class="button" id="discoverButton">Discover PRs</button><button class="button primary" id="importButton">＋ Import change</button></div></div>
    <div class="filterbar">
      <select class="field compact" id="statusFilter"><option value="">All statuses</option>${['Open','Draft','Approved','Changes Requested','Merged','Closed'].map(value=>`<option>${value}</option>`).join('')}</select>
      <input class="field compact" id="authorFilter" placeholder="Filter by author">
      <input class="field compact" id="reviewerFilter" placeholder="Filter by reviewer">
    </div>
    <div class="card" id="changeList">${state.changes.length?state.changes.map(changeRow).join(''):'<div class="empty">No Changes yet. Import an existing GitHub pull request or create one from Branches.</div>'}</div>`;
  el('importButton').onclick=openImport;
  el('discoverButton').onclick=openDiscover;
  el('refreshChanges').onclick=async()=>{state.changes=await api('/api/review/changes');renderChanges();showToast('Changes refreshed')};
  const filter=()=>{
    const status=el('statusFilter').value.toLowerCase(),author=el('authorFilter').value.toLowerCase(),reviewer=el('reviewerFilter').value.toLowerCase();
    const items=state.changes.filter(c=>(!status||c.status.toLowerCase()===status)&&(!author||c.author.toLowerCase().includes(author))&&(!reviewer||c.reviewers.some(r=>r.name.toLowerCase().includes(reviewer))));
    el('changeList').innerHTML=items.length?items.map(changeRow).join(''):'<div class="empty">No matching Changes.</div>';
  };
  el('statusFilter').onchange=filter;el('authorFilter').oninput=filter;el('reviewerFilter').oninput=filter;
}

function renderQueue(){
  crumbs(projectCrumb('Review <span>/</span> Review queue'));
  const waiting=state.changes.filter(c=>c.reviewers.some(r=>r.name===actor()&&r.status==='Requested'));
  const mine=state.changes.filter(c=>c.author===actor());
  const reviewed=state.changes.filter(c=>c.reviews.some(r=>r.reviewer===actor())).slice().sort((a,b)=>new Date(b.updatedAt)-new Date(a.updatedAt));
  const section=(title,items)=>`<div class="card queue-section"><div class="card-header"><h2>${title}</h2><span class="pill">${items.length}</span></div>${items.length?items.map(changeRow).join(''):'<div class="empty small">Nothing here.</div>'}</div>`;
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

function fileBreadcrumbs(path,reference){
  const parts=path?path.split('/').filter(Boolean):[];
  let acc='';
  const links=[`<button class="button text" data-route="/files?ref=${encodeURIComponent(reference)}">root</button>`];
  parts.forEach((part,index)=>{acc=acc?`${acc}/${part}`:part;const last=index===parts.length-1;links.push(last?`<span>${esc(part)}</span>`:`<button class="button text" data-route="/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(acc)}">${esc(part)}</button>`)});
  return links.join(' <span>/</span> ');
}

async function renderFiles(route){
  crumbs(projectCrumb('Code <span>/</span> Files'));
  if(!source())return renderNoSource();
  const query=new URLSearchParams(route.split('?')[1]||'');
  const path=query.get('path')||'',reference=query.get('ref')||source().defaultBranch;
  const tree=await api(`/api/source/repositories/${source().id}/tree?reference=${encodeURIComponent(reference)}&path=${encodeURIComponent(path)}`);
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Files</h1><p>${esc(source().repositoryId.owner)}/${esc(source().repositoryId.name)} · ${esc(reference)}</p><div class="breadcrumbs" style="margin-top:8px">${fileBreadcrumbs(path,reference)}</div></div>
    <select class="field compact" id="refPicker"><option>${esc(reference)}</option></select></div>
    <div class="card source-list">${path?`<button class="source-row" data-up="${esc(path.split('/').slice(0,-1).join('/'))}"><span>↰</span><strong>..</strong></button>`:''}
    ${tree.entries.map(entry=>`<button class="source-row" data-path="${esc(entry.path)}" data-kind="${entry.kind}"><span>${entry.kind==='directory'?'▱':'≡'}</span><strong>${esc(entry.name)}</strong><small>${entry.kind==='file'?`${entry.size} bytes`:''}</small></button>`).join('')}</div>`;
  const branches=await api(`/api/source/repositories/${source().id}/branches`);
  el('refPicker').innerHTML=branches.map(branch=>`<option ${branch.name===reference?'selected':''}>${esc(branch.name)}</option>`).join('');
  el('refPicker').onchange=()=>navigate(`/files?ref=${encodeURIComponent(el('refPicker').value)}`);
  document.querySelectorAll('[data-path]').forEach(button=>button.onclick=()=>button.dataset.kind==='directory'?navigate(`/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(button.dataset.path)}`):renderSourceFile(button.dataset.path,reference));
  const up=document.querySelector('[data-up]');if(up)up.onclick=()=>navigate(`/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(up.dataset.up)}`);
}

async function renderSourceFile(path,reference){
  const file=await api(`/api/source/repositories/${source().id}/file?reference=${encodeURIComponent(reference)}&path=${encodeURIComponent(path)}`);
  crumbs(projectCrumb(`Code <span>/</span> ${esc(path)}`));
  el('content').innerHTML=`<div class="list-page-header"><div>
    <h1 class="file-title">${esc(path.split('/').pop())}</h1>
    <p>${esc(reference)} · ${file.size} bytes · ${esc(file.sha.slice(0,7))}</p>
    <div class="breadcrumbs" style="margin-top:8px">${fileBreadcrumbs(path,reference)}</div>
  </div>
  <div class="header-actions">
    <button class="button" id="copyPath">Copy path</button>
    <button class="button" data-route="/files?ref=${encodeURIComponent(reference)}&path=${encodeURIComponent(path.split('/').slice(0,-1).join('/'))}">Back</button>
  </div></div>
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

function renderModules(){
  crumbs(projectCrumb('Modules'));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Runtime composition</h1><p>The dogfooding milestone runs Core, Review, and the GitHub connector.</p></div></div>
    <div class="card">${state.modules.map(module=>`<div class="module-card"><span class="module-logo">${esc(module.name[0])}</span><div><h3>${esc(module.name)}</h3><p>${module.capabilities.map(esc).join(' · ')}</p></div><span class="module-state">● Enabled</span></div>`).join('')}
    <div class="module-card"><span class="module-logo">G</span><div><h3>GitHub Connector</h3><p>Repository source · change source · real GitHub REST API</p></div><span class="module-state">● Available</span></div></div>`;
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
  const definitions=await api('/api/pipelines/definitions');
  const id=route.split('/')[2];
  const selected=id?definitions.find(d=>d.id===id):null;
  crumbs(projectCrumb(`Automation <span>/</span> Pipelines${selected?` <span>/</span> ${esc(selected.name)}`:''}`));
  el('content').innerHTML=`<div class="list-page-header"><div><h1>Pipelines</h1><p>Definitions that run on push, change open, or manual trigger.</p></div>
    <button class="button" id="refreshPipelines">Refresh</button></div>
    <div class="card">${definitions.map(def=>`<article class="pipeline-row">
      <span class="number">≋</span>
      <div><h3>${esc(def.name)}</h3><p>${esc(triggerLabels(def.triggers))} · ${def.jobs?.length||0} jobs · ${def.enabled?'enabled':'disabled'}</p></div>
      <div class="header-actions">
        <button class="button" data-edit-pipeline="${esc(def.id)}">View</button>
        <button class="button" data-toggle-pipeline="${esc(def.id)}" data-enabled="${def.enabled?'1':'0'}">${def.enabled?'Disable':'Enable'}</button>
        <button class="button danger" data-delete-pipeline="${esc(def.id)}">Delete</button>
        <button class="button primary" data-run-pipeline="${esc(def.id)}" ${def.enabled?'':'disabled'}>Run</button>
      </div>
    </article>`).join('')||'<div class="empty">No pipeline definitions.</div>'}</div>
    ${selected?`<div class="card" style="margin-top:18px"><div class="card-header"><h2>${esc(selected.name)}</h2><span class="pill">v${esc(selected.version)}</span></div>
      <div class="card-body">${(selected.jobs||[]).map((job,i)=>`<div class="side-stat"><span>${i+1}. ${esc(job.name)}</span><strong>${(job.steps||[]).length} steps · check: ${esc(job.checkName||job.name)}</strong></div>`).join('')}</div></div>`:''}`;
  el('refreshPipelines').onclick=()=>renderPipelines(route);
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
