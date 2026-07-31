const configuredApi = window.LAUNCHER_API || window.RUNTIME_CONFIG?.apiBaseUrl;
const API = (configuredApi || window.location.origin).replace(/\/+$/, "");
const $ = (id) => document.getElementById(id);
const views = [...document.querySelectorAll(".view")];

let token = localStorage.getItem("token") || sessionStorage.getItem("token");
let currentUser = null;
let previousPublicView = "homeView";
let pageOffset = 0;
const pageLimit = 25;
let searchTimer;
let toastTimer;
const launcherRequestId = new URLSearchParams(window.location.search).get("launcher_auth");

async function api(path, options = {}) {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (token) headers.Authorization = `Bearer ${token}`;

  let response;
  try {
    response = await fetch(`${API}${path}`, { ...options, headers });
  } catch {
    throw new Error("Сервер недоступен. Проверьте подключение и попробуйте снова.");
  }

  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (response.status === 401 && path !== "/api/auth/login") clearSession();
    throw new Error(typeof data.detail === "string" ? data.detail : `Ошибка HTTP ${response.status}`);
  }
  return data;
}

function showView(viewId) {
  views.forEach((view) => view.classList.toggle("active", view.id === viewId));
  if (viewId !== "supportView") previousPublicView = viewId;
  window.scrollTo({ top: 0, behavior: "smooth" });
}

function setAuthMode(mode) {
  const isLogin = mode === "login";
  $("loginForm").classList.toggle("hidden", !isLogin);
  $("registerForm").classList.toggle("hidden", isLogin);
  $("authTitle").textContent = isLogin ? "Авторизация" : "Регистрация";
  setMessage("authMessage", "");
  showView("authView");
  setTimeout(() => $(isLogin ? "loginUsername" : "registerUsername").focus(), 0);
}

function setMessage(id, text, success = false) {
  const element = $(id);
  element.textContent = text;
  element.classList.toggle("success", success);
}

function showToast(text) {
  clearTimeout(toastTimer);
  $("toast").textContent = text;
  $("toast").classList.add("show");
  toastTimer = setTimeout(() => $("toast").classList.remove("show"), 3200);
}

function setBusy(form, busy) {
  const button = form.querySelector('button[type="submit"]');
  button.disabled = busy;
  if (!button.dataset.label) button.dataset.label = button.textContent.trim();
  button.querySelector("span")
    ? (button.querySelector("span").textContent = busy ? "Подождите…" : button.dataset.label)
    : (button.textContent = busy ? "Подождите…" : button.dataset.label);
}

async function checkHealth() {
  const box = $("serviceCheck");
  box.classList.remove("online", "offline");
  $("serviceTitle").textContent = "Проверяем сервис";
  $("serviceDetail").textContent = "Соединение с сервером";
  try {
    const health = await api("/health");
    box.classList.add("online");
    $("serviceTitle").textContent = "Сервис доступен";
    $("serviceDetail").textContent = health.version ? `Версия API ${health.version}` : "Можно авторизоваться";
  } catch {
    box.classList.add("offline");
    $("serviceTitle").textContent = "Сервис недоступен";
    $("serviceDetail").textContent = "Повторите проверку позднее";
  }
}

$("loginForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  setMessage("authMessage", "");
  setBusy(form, true);
  try {
    const credentials = Object.fromEntries(new FormData(form));
    const data = await api("/api/auth/login", { method: "POST", body: JSON.stringify(credentials) });
    token = data.access_token;
    localStorage.removeItem("token");
    sessionStorage.removeItem("token");
    const storage = $("rememberMe").checked ? localStorage : sessionStorage;
    storage.setItem("token", token);
    if (!(await completeLauncherLogin())) await showDashboard();
  } catch (error) {
    setMessage("authMessage", error.message);
  } finally {
    setBusy(form, false);
  }
});

$("registerForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  setMessage("authMessage", "");
  setBusy(form, true);
  try {
    const user = Object.fromEntries(new FormData(form));
    await api("/api/auth/register", { method: "POST", body: JSON.stringify(user) });
    form.reset();
    setAuthMode("login");
    setMessage("authMessage", "Аккаунт создан. После активации доступа администратором можно входить в лаунчер.", true);
  } catch (error) {
    setMessage("authMessage", error.message);
  } finally {
    setBusy(form, false);
  }
});

async function showDashboard() {
  try {
    const me = await api("/api/users/me");
    currentUser = me;
    if ($("dashboardView").querySelector(".cabinet-shell")) {
      renderCabinet(me);
      showView("dashboardView");
      return;
    }
    $("welcome").textContent = `Привет, ${me.username}`;
    $("accessText").textContent = me.has_access ? "Активна" : "Неактивна";
    $("hwidText").textContent = me.hwid_bound ? "Устройство привязано" : "Не привязан";
    $("roleText").textContent = me.role === "admin" ? "Администратор" : "Пользователь";
    $("accessBadge").textContent = me.has_access ? "Доступ активен" : "Нет доступа";
    $("accessBadge").classList.toggle("off", !me.has_access);
    $("authNavLabel").textContent = "Кабинет";
    $("admin").classList.toggle("hidden", me.role !== "admin");
    showView("dashboardView");
    if (me.role === "admin") await Promise.all([loadUsers(), loadStats()]);
    renderCabinet(me);
  } catch (error) {
    logout(false);
    setMessage("authMessage", error.message);
  }
}

function renderCabinet(me) {
  const root = $("dashboardView");
  const registered = new Date(me.created_at).toLocaleString("ru-RU");
  const lastLogin = me.last_login_at ? new Date(me.last_login_at).toLocaleString("ru-RU") : "—";
  const uid = String(me.id).split("-")[0].toUpperCase();
  const products = me.has_access ? "Клиент MyCustom 1.16.5" : "Пока нет активных товаров";
  root.innerHTML = `
    <section class="cabinet-shell">
      <div class="cabinet-title"><span>ЛИЧНЫЙ КАБИНЕТ</span><h1>Личный кабинет</h1><p>Управляйте доступом к клиенту и настройками аккаунта.</p></div>
      <div class="cabinet-table">
        <div class="cabinet-row"><b>◉&nbsp; UID</b><span>${uid}</span></div>
        <div class="cabinet-row"><b>◇&nbsp; Логин</b><span>${escapeHtml(me.username)}</span></div>
        <div class="cabinet-row"><b>⚒&nbsp; Группа</b><span>${me.role === "admin" ? "Администратор" : "Пользователь"}</span></div>
        <div class="cabinet-row"><b>▣&nbsp; Дата регистрации</b><span>${registered}</span></div>
        <div class="cabinet-row"><b>▣&nbsp; Последний вход</b><span>${lastLogin}</span></div>
        <div class="cabinet-row"><b>✉&nbsp; E-mail</b><span>${escapeHtml(me.email)}</span></div>
        <div class="cabinet-row"><b>▣&nbsp; HWID</b><span>${me.hwid_bound ? "Устройство привязано" : "Устройство ещё не привязано"}</span><button data-cabinet="hwid">Сбросить</button></div>
        <div class="cabinet-row"><b>▣&nbsp; Купленные товары</b><span>${products}</span><button data-cabinet="products">Подробнее</button></div>
        <div class="cabinet-row"><b>⚿&nbsp; Активация ключа</b><input id="activationKey" placeholder="Введите ключ"><button data-cabinet="activate">Активировать</button></div>
      </div>
      <div class="cabinet-actions"><button data-cabinet="promos">Мои промокоды</button><button data-cabinet="products">Купленные товары</button><button data-cabinet="download">⇩ Скачать лаунчер</button><button data-cabinet="password">✎ Сменить пароль</button><button class="cabinet-logout" data-cabinet="logout">⇥ Выйти</button></div>
      <p id="cabinetMessage" class="message"></p>
    </section>`;
  root.querySelectorAll("[data-cabinet]").forEach((button) => button.addEventListener("click", () => {
    const action = button.dataset.cabinet;
    if (action === "logout") return logout();
    if (action === "download") return showToast("Файл лаунчера ещё не опубликован в разделе загрузок.");
    if (action === "hwid") return showToast("Сброс HWID выполняется через поддержку после проверки аккаунта.");
    if (action === "products") return showToast(me.has_access ? "У вас активен доступ к MyCustom 1.16.5." : "Активных товаров пока нет.");
    if (action === "promos") return showToast("У вас пока нет активных промокодов.");
    if (action === "password") return showToast("Для смены пароля обратитесь в поддержку.");
    if (action === "activate") {
      const key = root.querySelector("#activationKey").value.trim();
      showToast(key ? "Активация ключей ещё не подключена к API." : "Введите ключ активации.");
    }
  }));
}

async function completeLauncherLogin() {
  if (!launcherRequestId || !token) return false;
  try {
    await api("/api/launcher/web-auth/complete", {
      method: "POST",
      body: JSON.stringify({ request_id: launcherRequestId })
    });
    document.body.innerHTML = `<main class="auth-stage active"><div class="auth-panel launcher-confirm"><h1>Вход подтверждён</h1><p>Вернитесь в лаунчер — он продолжит авторизацию автоматически.</p></div></main>`;
    return true;
  } catch (error) {
    setMessage("authMessage", error.message);
    return false;
  }
}

async function loadUsers() {
  try {
    setMessage("dashMessage", "");
    const search = encodeURIComponent($("userSearch").value.trim());
    const page = await api(`/api/admin/users?offset=${pageOffset}&limit=${pageLimit}&search=${search}`);
    $("users").innerHTML = page.items.length
      ? page.items.map((user) => `
        <tr>
          <td><strong>${escapeHtml(user.username)}</strong><br><small>${escapeHtml(user.email)}</small></td>
          <td>${user.has_access ? "Активен" : "Отключён"}</td>
          <td>${user.hwid_bound ? "Привязан" : "—"}</td>
          <td>${user.last_login_at ? new Date(user.last_login_at).toLocaleString("ru-RU") : "—"}</td>
          <td>
            <button class="action ${user.has_access ? "danger" : "success"}" data-access="${user.id}" data-value="${!user.has_access}">${user.has_access ? "Отключить" : "Включить"}</button>
            <button class="action" data-reset="${user.id}">Сбросить HWID</button>
          </td>
        </tr>`).join("")
      : '<tr><td colspan="5">Пользователи не найдены</td></tr>';
    $("userCount").textContent = `Найдено: ${page.total}`;
    $("pageInfo").textContent = `${Math.floor(page.offset / page.limit) + 1} / ${Math.max(1, Math.ceil(page.total / page.limit))}`;
    $("prevPage").disabled = page.offset === 0;
    $("nextPage").disabled = page.offset + page.limit >= page.total;
  } catch (error) {
    setMessage("dashMessage", error.message);
  }
}

async function loadStats() {
  try {
    const stats = await api("/api/admin/stats");
    $("statTotal").textContent = stats.total_users;
    $("statActive").textContent = stats.active_users;
    $("statBound").textContent = stats.bound_devices;
    $("statAdmins").textContent = stats.administrators;
  } catch (error) {
    setMessage("dashMessage", error.message);
  }
}

function escapeHtml(value) {
  const element = document.createElement("div");
  element.textContent = value ?? "";
  return element.innerHTML;
}

function clearSession() {
  token = null;
  currentUser = null;
  localStorage.removeItem("token");
  sessionStorage.removeItem("token");
  $("authNavLabel").textContent = "Авторизация";
  $("admin")?.classList.add("hidden");
}

function logout(notify = true) {
  clearSession();
  setAuthMode("login");
  if (notify) showToast("Вы вышли из аккаунта");
}

$("showRegister").addEventListener("click", () => setAuthMode("register"));
$("showLogin").addEventListener("click", () => setAuthMode("login"));
$("retryHealth").addEventListener("click", checkHealth);
$("forgotPassword").addEventListener("click", () => {
  showView("supportView");
  showToast("Для восстановления доступа напишите в поддержку");
});
$("buySupport").addEventListener("click", () => showView("supportView"));
$("supportBack").addEventListener("click", () => showView(token ? "dashboardView" : previousPublicView));
$("brandButton").addEventListener("click", () => showView("homeView"));
$("authNav").addEventListener("click", () => token ? showDashboard() : setAuthMode("login"));
$("downloadLauncher").addEventListener("click", () => showToast("Файл лаунчера ещё не опубликован."));
document.querySelectorAll("[data-view]").forEach((button) => button.addEventListener("click", () => showView(button.dataset.view)));
$("logout").addEventListener("click", () => logout());
$("refresh").addEventListener("click", async () => {
  await Promise.all([loadUsers(), loadStats()]);
  showToast("Данные обновлены");
});
$("userSearch").addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => { pageOffset = 0; loadUsers(); }, 250);
});
$("prevPage").addEventListener("click", () => { pageOffset = Math.max(0, pageOffset - pageLimit); loadUsers(); });
$("nextPage").addEventListener("click", () => { pageOffset += pageLimit; loadUsers(); });
$("users").addEventListener("click", async (event) => {
  const button = event.target.closest("button");
  if (!button) return;
  button.disabled = true;
  try {
    if (button.dataset.access) {
      await api(`/api/admin/users/${button.dataset.access}/access`, {
        method: "PATCH",
        body: JSON.stringify({ has_access: button.dataset.value === "true" })
      });
      showToast("Статус доступа изменён");
    }
    if (button.dataset.reset) {
      await api("/api/admin/hwid-reset", { method: "POST", body: JSON.stringify({ user_id: button.dataset.reset }) });
      showToast("Привязка HWID сброшена");
    }
    await Promise.all([loadUsers(), loadStats()]);
  } catch (error) {
    setMessage("dashMessage", error.message);
  } finally {
    button.disabled = false;
  }
});

const topbar = document.querySelector(".topbar");
const updateTopbar = () => topbar.classList.toggle("scrolled", window.scrollY > 24);
window.addEventListener("scroll", updateTopbar, { passive: true });
updateTopbar();

const revealObserver = "IntersectionObserver" in window
  ? new IntersectionObserver((entries, observer) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: "0px 0px -45px" })
  : null;
document.querySelectorAll(".reveal").forEach((element) => {
  if (revealObserver) revealObserver.observe(element);
  else element.classList.add("is-visible");
});

checkHealth();
if (token) completeLauncherLogin().then((completed) => { if (!completed) showDashboard(); });
