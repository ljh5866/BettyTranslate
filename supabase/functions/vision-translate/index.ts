// Supabase Edge Function：视觉图片翻译代理
// -------------------------------------------------------------
// 目的：把开发者预置的 DeepSeek API Key 彻底移出客户端，防止被
// 抓包泄漏/滥用。客户端只携带登录态（JWT）调用本函数，由服务端：
//   1) 校验登录用户（auth.uid()）
//   2) 校验该账号免费额度（user_usage.image_translate_count < 15）
//   3) 用服务端 secret 里的 DEEPSEEK_API_KEY 调 DeepSeek 视觉模型
//   4) 返回与客户端 DeepSeekVisionTranslator 一致的 regions JSON，
//      并服务端累加计数
//
// 部署（需安装 Supabase CLI，用户手动执行）：
//   supabase functions deploy vision-translate
//   supabase secrets set DEEPSEEK_API_KEY=sk-xxxx
// -------------------------------------------------------------

// @ts-nocheck — Deno 全局
import { createClient } from "npm:@supabase/supabase-js@2";

const DEEPSEEK_ENDPOINT = "https://api.deepseek.com/chat/completions";
const DEEPSEEK_MODEL = "deepseek-v4-flash-vision-exp";
const FREE_LIMIT = 15; // 与客户端 App.FreeImageTranslateLimit 保持一致

/** 与客户端 DeepSeekVisionTranslator.BuildPrompt 一致的提示词 */
function buildPrompt(): string {
  return `你是图像文字翻译助手。请识别图片中的英文文本并翻译成简体中文，最终只输出一个 JSON 对象，不要输出任何其他文字或解释：
{"regions":[{"text":"英文原文","translation":"简体中文译文","cx":中心X百分比,"cy":中心Y百分比,"w":宽百分比,"h":高百分比}]}

严格要求：
1. 只识别【英文】文本。本身就是中文的文字、纯数字、坐标（如 (43,260)）、纯数字统计（如 358 个评价）一律不要放进 regions。
2. 若某段英文被翻译后结果仍全是英文（如人名/专有名词 MattBny、Konsta★Starlight、VSLib），该区域【不要】输出。
3. 英文夹中文时，只翻译英文部分，中文保持原样，输出合并后的整段中文（例如 "创作者:MattBny" → translation 为 "创作者:MattBny"）。若该段除专有名词外没有英文，不要输出。
4. 坐标用图片宽/高的百分比（0~100 的数，可带小数）。包围盒要【略宽松】，让盒子上下左右都比文字再多出约 4% 的空白边距，确保足够盖住英文。
5. 同一行、同一句、同一按钮上相邻的英文合并成一个 region，不要把一句话拆成多块。
6. regions 按图片从上到下、从左到右排列。不要遗漏任何需要翻译的英文。`;
}

/** 从 DeepSeek 原始响应里取出 choices[0].message.content */
function extractContent(body: string): string {
  const data = JSON.parse(body);
  const content = data?.choices?.[0]?.message?.content;
  if (typeof content !== "string" || content.length === 0) {
    throw new Error("DeepSeek 未返回文本内容");
  }
  return content;
}

/** 去掉模型可能包进去的 ```json ... ``` 代码块，只留 JSON */
function stripFences(text: string): string {
  let t = text.trim();
  if (t.startsWith("```")) {
    const nl = t.indexOf("\n");
    if (nl >= 0) t = t.slice(nl + 1);
    const end = t.lastIndexOf("```");
    if (end >= 0) t = t.slice(0, end).trim();
  }
  const start = t.indexOf("{");
  if (start > 0) t = t.slice(start);
  const end = t.lastIndexOf("}");
  if (end >= 0 && end < t.length - 1) t = t.slice(0, end + 1);
  return t;
}

/** 调 DeepSeek，带 response_format 与去 response_format 重试逻辑 */
async function callDeepSeek(imageBase64: string): Promise<string> {
  const messages = [
    {
      role: "user",
      content: [
        { type: "text", text: buildPrompt() },
        { type: "image_url", image_url: { url: `data:image/jpeg;base64,${imageBase64}` } },
      ],
    },
  ];

  const buildPayload = (useResponseFormat: boolean) => {
    const payload: Record<string, unknown> = {
      model: DEEPSEEK_MODEL,
      messages,
      temperature: 0.2,
      max_tokens: 4000,
      thinking: { type: "disabled" },
    };
    if (useResponseFormat) payload.response_format = { type: "json_object" };
    return payload;
  };

  const doPost = async (useResponseFormat: boolean) => {
    return await fetch(DEEPSEEK_ENDPOINT, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${Deno.env.get("DEEPSEEK_API_KEY")}`,
      },
      body: JSON.stringify(buildPayload(useResponseFormat)),
    });
  };

  let resp = await doPost(true);
  if (resp.status === 400 || resp.status === 422) {
    resp = await doPost(false);
  }

  const body = await resp.text();
  if (!resp.ok) {
    throw new Error(`DeepSeek 接口返回 ${resp.status}：${body}`);
  }
  return extractContent(body);
}

Deno.serve(async (req: Request) => {
  // 统一 JSON 响应
  const json = (obj: unknown, status = 200) =>
    new Response(JSON.stringify(obj), {
      status,
      headers: { "Content-Type": "application/json" },
    });

  try {
    const authHeader = req.headers.get("Authorization") ?? "";
    const token = authHeader.startsWith("Bearer ") ? authHeader.slice(7) : "";
    if (!token) return json({ error: "未登录，请先登录后再使用图片翻译" }, 401);

    // 用登录态创建客户端（会上携带用户 JWT，使 user_usage 表的 RLS 生效，
    // 用户只能读取/写入自己的额度记录）
    const client = createClient(
      Deno.env.get("SUPABASE_URL")!,
      Deno.env.get("SUPABASE_ANON_KEY")!,
      { global: { headers: { Authorization: authHeader } } },
    );

    // 校验登录态并拿到 auth.uid()
    const { data: { user }, error: userError } = await client.auth.getUser(token);
    if (userError || !user) return json({ error: "登录已失效，请重新登录" }, 401);
    const uid = user.id;

    const { image_base64 } = await req.json();
    if (!image_base64 || typeof image_base64 !== "string") {
      return json({ error: "请求缺少图片数据" }, 400);
    }

    // 免费额度校验（服务端为准，客户端侧仅仅提前提示）
    // is_unlimited = true 的特权账号（由管理后台在 user_usage 表维护）免受限次限制，不校验次数、不累加计数
    const { data: usage } = await client
      .from("user_usage")
      .select("image_translate_count, is_unlimited")
      .eq("user_id", uid)
      .maybeSingle();
    const isPrivileged = usage?.is_unlimited === true;
    const used = usage?.image_translate_count ?? 0;
    if (!isPrivileged && used >= FREE_LIMIT) {
      return json(
        { error: `免费截图翻译体验已用完（共 ${FREE_LIMIT} 次），请前往「设置」填写你自己的 DeepSeek API Key 后继续使用` },
        403,
      );
    }

    if (!Deno.env.get("DEEPSEEK_API_KEY")) {
      return json({ error: "服务端未配置 DeepSeek API Key，请联系开发者" }, 500);
    }

    // 调 DeepSeek 视觉模型
    const content = await callDeepSeek(image_base64);
    const regions = JSON.parse(stripFences(content));

    // 翻译成功后服务端累加免费计数（仅记录，不影响返回结果；特权账号不计、无限使用）
    if (!isPrivileged) {
      await client.from("user_usage").upsert(
        { user_id: uid, image_translate_count: used + 1 },
        { onConflict: "user_id" },
      );
    }

    // 返回与客户端 ParseRegions 兼容的 { regions: [...] }
    return json({ regions: regions?.regions ?? [] });
  } catch (e) {
    return json({ error: (e as Error)?.message ?? String(e) }, 500);
  }
});
