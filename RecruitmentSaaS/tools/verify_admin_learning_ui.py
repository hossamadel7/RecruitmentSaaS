"""Only the isolated BrowserHost --admin-demo; fictional data and strict no-send transport."""
import json
import time
import uuid
from pathlib import Path
import urllib.request
from playwright.sync_api import sync_playwright

ORIGIN = "http://127.0.0.1:5189"
with urllib.request.urlopen(ORIGIN + "/validation/health", timeout=10) as response:
    if b"Synthetic database only" not in response.read():
        raise RuntimeError("Synthetic BrowserHost required")
output = Path(__file__).resolve().parents[1] / "docs/validation"
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width":1440,"height":1000})
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.goto(ORIGIN + "/validation/login")
    demo = page.request.get(ORIGIN + "/validation/admin-demo").json()
    chat = demo["conversationId"]
    page.goto(ORIGIN + "/Inbox")
    with page.expect_response(ORIGIN + '/api/conversations/' + chat):
        page.locator(f'.conversation-row[data-id="{chat}"]').click()
    page.wait_for_timeout(300)
    page.evaluate("Inbox.openTransfer()")
    page.wait_for_selector('.transfer-item[data-ai="true"]')
    assert "SalesAgent" in page.locator('.transfer-item[data-ai="true"]').inner_text()
    page.screenshot(path=str(output / "ai-inbox-transfer.png"))
    page.locator('.transfer-item[data-ai="true"]').click()
    with page.expect_response("**/api/ai-sales/conversations/*/assign") as assigned:
        page.locator('#tc-ok').click()
    assert assigned.value.ok, assigned.value.text()
    page.wait_for_selector('a:has-text("اسأل المدير")')
    page.goto(ORIGIN + "/AiSalesTraining/AdminChannel?conversationId=" + chat)
    page.wait_for_selector('#admin-account option', state="attached")
    page.locator('#admin-phone').fill('+201099900011')
    page.locator('#admin-bind button').click()
    page.wait_for_function("document.querySelector('#admin-code').textContent.includes('LINK-')")
    code = page.locator('#admin-code').inner_text().split('LINK-')[1].strip()

    def inbound(text):
        response = page.request.post(ORIGIN + "/validation/admin-inbound", data={
            "from":"201099900011", "id":"wamid.synthetic."+str(uuid.uuid4()), "type":"text",
            "timestamp":str(int(time.time())), "text":{"body":text}})
        assert response.ok, response.text()

    inbound('LINK-'+code)
    page.locator('#admin-refresh').click()
    page.wait_for_function("document.querySelector('#admin-status').textContent.includes('موثقة')")
    assert not page.locator('#admin-bind').is_visible()
    page.locator('#admin-ask button').click()
    page.wait_for_selector('#admin-requests article')
    question = page.locator('#admin-requests h3').first.inner_text().split(' — ')[0]
    inbound(question + ' موافقة خاصة للعميل ده فقط')
    page.locator('#admin-refresh').click()
    page.wait_for_function("document.querySelector('#admin-requests textarea').value.includes('موافقة خاصة')")
    page.get_by_role('button',name='اعتماد الإجابة بعد المراجعة').click()
    page.get_by_role('button',name='تجهيز مسودة العميل للمراجعة').click()
    page.wait_for_selector('a:has-text("مراجعة مسودة الرد")')
    assert not page.locator('#admin-error').is_visible(), page.locator('#admin-error').inner_text()
    page.evaluate("document.querySelector('#admin-code').textContent='' ")
    page.screenshot(path=str(output / "ai-admin-channel-desktop.png"), full_page=True)
    page.set_viewport_size({"width":390,"height":844})
    page.reload()
    page.wait_for_selector('a:has-text("مراجعة مسودة الرد")')
    page.evaluate('window.scrollTo(0,0)')
    page.wait_for_timeout(700)
    page.screenshot(path=str(output / "ai-admin-channel-mobile.png"), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth + 2')
    assert page.locator('main.main-content').bounding_box()['width'] >= 380
    assert not errors, errors
    print(json.dumps({"passed":True,"flows":["Inbox AI transfer","Admin phone verification","private reply correlation","customer-only review","draft preparation"],"pageErrors":errors}))
    browser.close()
