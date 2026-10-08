-- TimePlanner 云端（多用户共用一份计划）
-- 用法：Supabase 控制台 → SQL Editor → 新建查询 → 整段粘贴 → Run。跑一次就够，重复跑也安全。
--
-- 说明：
--   · 身份用 Supabase 自带的 auth.users（注册/登录/改密码都别自己写）；
--   · 一个 workspace = 一份大家共用的计划，邀请码入伙；
--   · 任务/项目都带 rev 与 updated_at（并发用）、deleted_at（软删，不真删）；
--   · 所有权限靠 RLS，exe 里带的 anon key 被抓包也无所谓。

create extension if not exists pgcrypto;

-- ---------- 表 ----------

create table if not exists public.profiles (
  id    uuid primary key references auth.users(id) on delete cascade,
  email text,
  name  text
);

create table if not exists public.workspaces (
  id          uuid primary key default gen_random_uuid(),
  name        text not null default '我们的计划',
  invite_code text not null unique default encode(gen_random_bytes(6), 'hex'),
  created_by  uuid default auth.uid(),
  created_at  timestamptz not null default now()
);

create table if not exists public.members (
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  user_id      uuid not null references auth.users(id) on delete cascade,
  role         text not null default 'member',          -- owner | member
  joined_at    timestamptz not null default now(),
  primary key (workspace_id, user_id)
);

create table if not exists public.tasks (
  id           text primary key,                        -- 沿用本地 data.json 里的 id，两边同一个
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  title        text not null default '',
  note         text not null default '',
  date         date not null default current_date,
  done         boolean not null default false,
  done_at      timestamptz,
  priority     int  not null default 0,
  tag          text not null default '',
  sort         int  not null default 0,
  project_id   text not null default '',
  rev          int  not null default 1,
  updated_at   timestamptz not null default now(),
  deleted_at   timestamptz,
  created_by   uuid default auth.uid(),
  completed_by uuid
);
create index if not exists tasks_ws_idx on public.tasks(workspace_id, updated_at);

create table if not exists public.projects (
  id           text primary key,
  workspace_id uuid not null references public.workspaces(id) on delete cascade,
  parent_id    text not null default '',
  kind         int  not null default 0,
  item_id      text not null default '',
  sort         int  not null default 0,
  open         boolean not null default true,
  title        text not null default '',
  steps        int  not null default 0,
  reached      int  not null default 0,
  rev          int  not null default 1,
  updated_at   timestamptz not null default now(),
  deleted_at   timestamptz,
  created_by   uuid default auth.uid()
);
create index if not exists projects_ws_idx on public.projects(workspace_id, updated_at);

-- ---------- 服务端自己维护的列 ----------

-- updated_at 一律由服务端写：几台机器的钟不一定一样，谁新谁旧必须有个统一裁判。
create or replace function public.tp_touch() returns trigger
language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

drop trigger if exists tasks_touch on public.tasks;
create trigger tasks_touch before insert or update on public.tasks
  for each row execute function public.tp_touch();

drop trigger if exists projects_touch on public.projects;
create trigger projects_touch before insert or update on public.projects
  for each row execute function public.tp_touch();

-- 建人：客户端推送时总是带着 created_by，这里强制保留原来的作者，免得被后来者覆盖。
create or replace function public.tp_keep_creator() returns trigger
language plpgsql as $$
begin
  if (tg_op = 'UPDATE') then
    new.created_by := old.created_by;
  end if;
  if (new.created_by is null) then
    new.created_by := auth.uid();
  end if;
  return new;
end $$;

drop trigger if exists tasks_creator on public.tasks;
create trigger tasks_creator before insert or update on public.tasks
  for each row execute function public.tp_keep_creator();

drop trigger if exists projects_creator on public.projects;
create trigger projects_creator before insert or update on public.projects
  for each row execute function public.tp_keep_creator();

-- 注册时自动补一条 profile（成员列表要显示名字，public 里读不到 auth.users 的邮箱）
create or replace function public.tp_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles(id, email, name)
  values (new.id, new.email, coalesce(new.raw_user_meta_data->>'name', split_part(new.email, '@', 1)))
  on conflict (id) do nothing;
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created after insert on auth.users
  for each row execute function public.tp_new_user();

-- ---------- 权限判定（必须 SECURITY DEFINER） ----------
-- 在 members 表的策略里直接查 members 会导致 Postgres 报「infinite recursion detected in policy」，
-- 绕开的唯一办法就是走一个 SECURITY DEFINER 函数。

create or replace function public.tp_is_member(ws uuid) returns boolean
language sql security definer stable set search_path = public as $$
  select exists (
    select 1 from public.members m
    where m.workspace_id = ws and m.user_id = auth.uid()
  );
$$;

create or replace function public.tp_is_owner(ws uuid) returns boolean
language sql security definer stable set search_path = public as $$
  select exists (
    select 1 from public.members m
    where m.workspace_id = ws and m.user_id = auth.uid() and m.role = 'owner'
  );
$$;

-- ---------- RLS ----------

alter table public.profiles   enable row level security;
alter table public.workspaces enable row level security;
alter table public.members    enable row level security;
alter table public.tasks      enable row level security;
alter table public.projects   enable row level security;

drop policy if exists profiles_self on public.profiles;
create policy profiles_self on public.profiles for select
  using (id = auth.uid() or exists (
    select 1 from public.members me
    join public.members other on other.workspace_id = me.workspace_id
    where me.user_id = auth.uid() and other.user_id = profiles.id));
drop policy if exists profiles_self_upd on public.profiles;
create policy profiles_self_upd on public.profiles for update using (id = auth.uid());

drop policy if exists ws_read on public.workspaces;
create policy ws_read on public.workspaces for select using (public.tp_is_member(id));
drop policy if exists ws_upd on public.workspaces;
create policy ws_upd on public.workspaces for update using (public.tp_is_owner(id));

drop policy if exists mem_read on public.members;
create policy mem_read on public.members for select using (public.tp_is_member(workspace_id));
drop policy if exists mem_del on public.members;
create policy mem_del on public.members for delete
  using (public.tp_is_owner(workspace_id) or user_id = auth.uid());

drop policy if exists tasks_rw on public.tasks;
create policy tasks_rw on public.tasks for all
  using (public.tp_is_member(workspace_id))
  with check (public.tp_is_member(workspace_id));

drop policy if exists projects_rw on public.projects;
create policy projects_rw on public.projects for all
  using (public.tp_is_member(workspace_id))
  with check (public.tp_is_member(workspace_id));

-- ---------- 建 / 加入工作区 ----------

create or replace function public.tp_create_workspace(nm text) returns json
language plpgsql security definer set search_path = public as $$
declare w public.workspaces;
begin
  if auth.uid() is null then raise exception '请先登录'; end if;
  insert into public.workspaces(name)
  values (coalesce(nullif(trim(nm), ''), '我们的计划'))
  returning * into w;
  insert into public.members(workspace_id, user_id, role) values (w.id, auth.uid(), 'owner');
  return json_build_object('id', w.id, 'name', w.name, 'invite', w.invite_code, 'role', 'owner');
end $$;

create or replace function public.tp_join_workspace(code text) returns json
language plpgsql security definer set search_path = public as $$
declare w public.workspaces;
begin
  if auth.uid() is null then raise exception '请先登录'; end if;
  select * into w from public.workspaces
  where invite_code = lower(trim(coalesce(code, '')));
  if w.id is null then raise exception '邀请码不对'; end if;
  insert into public.members(workspace_id, user_id, role)
  values (w.id, auth.uid(), 'member')
  on conflict (workspace_id, user_id) do nothing;
  return json_build_object('id', w.id, 'name', w.name, 'invite', w.invite_code, 'role', 'member');
end $$;

create or replace function public.tp_my_workspaces() returns json
language sql security definer stable set search_path = public as $$
  select coalesce(json_agg(json_build_object(
           'id', w.id, 'name', w.name, 'invite', w.invite_code, 'role', m.role
         ) order by m.joined_at), '[]'::json)
  from public.members m
  join public.workspaces w on w.id = m.workspace_id
  where m.user_id = auth.uid();
$$;

create or replace function public.tp_members(ws uuid) returns json
language sql security definer stable set search_path = public as $$
  select coalesce(json_agg(json_build_object(
           'id', m.user_id, 'role', m.role,
           'name', coalesce(p.name, p.email, '成员'),
           'email', p.email
         ) order by m.joined_at), '[]'::json)
  from public.members m
  left join public.profiles p on p.id = m.user_id
  where m.workspace_id = ws and public.tp_is_member(ws);
$$;

-- ---------- 授权 ----------

grant usage on schema public to anon, authenticated;
grant select, insert, update, delete on public.tasks      to authenticated;
grant select, insert, update, delete on public.projects   to authenticated;
grant select, update on public.profiles                   to authenticated;
grant select, update on public.workspaces                 to authenticated;
grant select, delete on public.members                    to authenticated;

grant execute on function public.tp_create_workspace(text) to authenticated;
grant execute on function public.tp_join_workspace(text)   to authenticated;
grant execute on function public.tp_my_workspaces()        to authenticated;
grant execute on function public.tp_members(uuid)          to authenticated;
grant execute on function public.tp_is_member(uuid)        to authenticated;
grant execute on function public.tp_is_owner(uuid)         to authenticated;

-- ---------- 收尾 ----------
-- PostgREST 有一份自己的表结构缓存。刚建完表，API 可能还回
-- 「Could not find the table 'public.tasks' in the schema cache」(PGRST205)，
-- 就是缓存没刷。手动喊一声，省得你以为建表失败了。
notify pgrst, 'reload schema';
