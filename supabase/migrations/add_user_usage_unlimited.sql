-- 图片翻译特权字段：is_unlimited = true 的账号可无限次使用图片翻译。
-- 该字段由管理后台（Directus 等以 service_role / 管理员数据库角色连接）维护，
-- 普通登录用户不可修改，避免用户把自己「自我提权」为无限特权。

alter table public.user_usage
  add column if not exists is_unlimited boolean not null default false;

-- 收紧权限：普通用户只能读写【自己】那一条记录的计数，不能修改/指定特权字段。
-- 先撤销表级 insert、update，再按列授权，把 is_unlimited 排除在用户可写范围外。
revoke update on public.user_usage from authenticated;
revoke insert on public.user_usage from authenticated;

grant update (user_id, image_translate_count) on public.user_usage to authenticated;
grant insert (user_id, image_translate_count) on public.user_usage to authenticated;
