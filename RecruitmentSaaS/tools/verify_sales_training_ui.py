"""Exercise the isolated BrowserHost --training-demo. Never connects to the live CRM."""
import json
from pathlib import Path
import urllib.request
from playwright.sync_api import sync_playwright

ORIGIN = "http://127.0.0.1:5189"
with urllib.request.urlopen(ORIGIN + "/validation/health", timeout=10) as response:
    if b"Synthetic database only" not in response.read():
        raise RuntimeError("Only the isolated synthetic BrowserHost is allowed.")
output = Path(__file__).resolve().parents[1] / "docs/validation"
output.mkdir(exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width":1440,"height":1000})
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    try:
        page.goto(ORIGIN + "/validation/login")
        page.goto(ORIGIN + "/AiSalesTraining/Index")
        page.wait_for_selector("#training-account option", state="attached")
        page.locator("#training-open").check()
        page.locator("#training-resume").click()
        page.wait_for_function("document.querySelector('#training-status').textContent.includes('نشط')")
        page.locator("#training-scan").click()
        page.wait_for_selector("#training-analyses summary")
        page.locator("#training-analyses summary").first.click()
        page.locator("#training-analyses button").first.click()
        page.wait_for_selector("#training-knowledge article")
        card = page.locator("#training-knowledge article").first
        card.locator("input:not([type])").first.fill("قاعدة تجريبية معتمدة")
        card.locator("textarea").fill("مثال خيالي فقط: اسأل سؤالا واحدا للتوضيح.")
        card.locator("input[type=checkbox]").check()
        card.get_by_role("button", name="اعتماد المحتوى المصحح").click()
        page.wait_for_function("document.querySelector('#training-knowledge').textContent.includes('تعطيل هذه المعلومة')")
        with page.expect_response("**/api/ai-sales-training/index?*") as indexed:
            page.locator("#training-index").click()
        assert indexed.value.ok
        page.locator("#training-query").fill("سؤال للتوضيح")
        page.locator("#training-search button").click()
        page.wait_for_function("document.querySelector('#training-matches').textContent.length>10")
        page.locator("#case-dataset").fill("browser-fixed-v1")
        page.locator("#case-scenario").fill("clarification")
        page.locator("#case-question").fill("ممكن أعرف التفاصيل؟")
        page.locator("#case-expected").fill("رد قصير بلا ادعاء معلومات غير معتمدة")
        page.locator("#case-privacy").check()
        page.locator("#training-case").get_by_role("button", name="إضافة حالة", exact=True).click()
        page.wait_for_function("document.querySelector('#training-status').textContent.includes('إضافة الحالة')")
        for name in ["baseline", "candidate"]:
            page.locator("#run-dataset").fill("browser-fixed-v1")
            page.locator("#run-config").fill(name)
            page.locator("#run-baseline").fill("baseline")
            page.locator("#training-run button").click()
            page.wait_for_function("document.querySelector('#training-status').textContent.includes('اكتمل تشغيل')")
            page.locator("#training-status").evaluate("e=>e.textContent='ready'")
        page.locator("#evaluation-dataset").fill("browser-fixed-v1")
        page.locator("#evaluation-baseline").fill("baseline")
        page.locator("#evaluation-candidate").fill("candidate")
        page.locator("#training-compare button").click()
        page.wait_for_selector("#training-results article")
        assert page.locator("#training-results article").count() == 2
        for card in page.locator("#training-results article").all():
            card.locator("textarea").fill("تقييم تجريبي: لا ادعاء حقيقة ولا إرسال للعميل")
            with page.expect_response("**/api/ai-sales-training/evaluations/*/score") as scored:
                card.get_by_role("button", name="اجتاز المراجعة").click()
            assert scored.value.ok
        page.locator("#training-compare button").click()
        page.wait_for_function("document.querySelector('#training-comparison').textContent.includes('\"fullyEvaluated\": true')")
        assert not errors, errors
        assert page.locator("#training-error").is_hidden(), page.locator("#training-error").inner_text()
        page.evaluate("window.scrollTo(0,0)")
        page.wait_for_timeout(700)
        page.screenshot(path=str(output / "sales-training-desktop.png"), full_page=True)
        page.set_viewport_size({"width":390,"height":844})
        page.evaluate("window.scrollTo(0,0)")
        page.wait_for_timeout(700)
        assert page.locator('main.main-content').bounding_box()['width'] >= 380
        page.screenshot(path=str(output / "sales-training-mobile.png"), full_page=True)
        print(json.dumps({"passed":True,"flows":["resume","scan","analyse","review/approve","index","search explanation",
            "fixed case","baseline/candidate evaluation","human scoring","comparison"],"pageErrors":errors}))
    except Exception:
        page.screenshot(path=str(output / "sales-training-failure.png"), full_page=True)
        raise
    finally:
        browser.close()
