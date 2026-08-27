-- 为 user_usage 表配置行级安全（RLS）策略：
-- 仅允许登录用户读取 / 写入【自己】的免费额度记录，确保
-- 服务端 Edge Function 能按用户累加 image_translate_count，
-- 客户端也能查询到自己的已用次数，从而真正限制免费滥用。
-- 幂等写法：先删同名策略再重建，避免重复部署时报错。

drop policy if exists "user_usage_select_own" on public.user_usage;
drop policy if exists "user_usage_insert_own" on public.user_usage;
drop policy if exists "user_usage_update_own" on public.user_usage;

create policy "user_usage_select_own" on public.user_usage
  for select using (auth.uid() = user_id);

create policy "user_usage_insert_own" on public.user_usage
  for insert with check (auth.uid() = user_id);

create policy "user_usage_update_own" on public.user_usage
  for update using (auth.uid() = user_id) with check (auth.uid() = user_id);
