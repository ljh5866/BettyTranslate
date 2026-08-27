-- 图片翻译免费额度表：按 Supabase 账号记录已使用的图片翻译次数。
-- 前 15 次免费使用开发者预置的 DeepSeek API；用尽后需用户填写自己的 Key。

create table if not exists public.user_usage (
  user_id uuid primary key references auth.users(id) on delete cascade,
  image_translate_count integer not null default 0
);

alter table public.user_usage enable row level security;

-- 用户只能读取、插入、更新自己那条计数（auth.uid() 来自当前登录态）
create policy "user_usage_select_own" on public.user_usage
  for select to authenticated using (auth.uid() = user_id);

create policy "user_usage_insert_own" on public.user_usage
  for insert to authenticated with check (auth.uid() = user_id);

create policy "user_usage_update_own" on public.user_usage
  for update to authenticated using (auth.uid() = user_id);

grant select, insert, update, delete on public.user_usage to authenticated;
