// 手机网页的配置。anon key 本来就是公开的（设计如此）：真正挡住别人的是 Supabase 的 RLS，
// 只有「这份计划里的成员」才读得到数据。桌面端仍然不把这个 key 放进 exe / 仓库。
window.TP_CONFIG = {
  url: 'https://qeaezpsjnkhdvhxmrpgb.supabase.co',
  key: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InFlYWV6cHNqbmtoZHZoeG1ycGdiIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTE0NTM3ODYsImV4cCI6MjEwNzAyOTc4Nn0.8aDJ9WmLh2lYHEfx3M1RcpjPXe_7nuVRjT7eH7jd5OM'
};