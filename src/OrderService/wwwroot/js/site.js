// Minimal frontend glue for the dashboard (optimized)
const apiBase = '';

// State
let items = [];
let allOrders = [];
let currentUser = null; // { id, email, role }

const statuses = {
    1: 'Pending',
    2: 'Processing',
    3: 'Shipped',
    4: 'Delivered',
    5: 'Cancelled'
};

// Cache commonly used DOM nodes
const dom = {
    itemsList: () => document.getElementById('itemsList'),
    orderTotal: () => document.getElementById('orderTotal'),
    productSelect: () => document.getElementById('productSelect'),
    qty: () => document.getElementById('qty'),
    email: () => document.getElementById('email'),
    ordersContainer: () => document.getElementById('ordersContainer'),
    filterStatus: () => document.getElementById('filterStatus'),
    searchInput: () => document.getElementById('searchInput'),
    addBtn: () => document.getElementById('addBtn'),
    clearBtn: () => document.getElementById('clearBtn'),
    orderForm: () => document.getElementById('orderForm'),
    refreshBtn: () => document.getElementById('refreshBtn'),
    loginBtn: () => document.getElementById('loginBtn'),
    logoutBtn: () => document.getElementById('logoutBtn'),
    userRole: () => document.getElementById('userRole')
};

function parseProductValue(val) {
    const [name, price] = val.split('|');
    return { name, price: parseFloat(price) };
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/[&<>"']/g, s => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[s]);
}

// Unified fetch helper that injects stored Basic auth and returns the Response
async function apiFetch(path, opts = {}) {
    const url = apiBase + path;
    const credentials = opts.credentials ?? 'same-origin';

    const headers = new Headers(opts.headers || {});
    try {
        const auth = sessionStorage.getItem('basicAuth');
        if (auth) headers.set('Authorization', auth);
    } catch (e) { /* ignore sessionStorage errors */ }

    const finalOpts = { ...opts, credentials, headers };
    return fetch(url, finalOpts);
}

// Items UI
function renderItems() {
    const ul = dom.itemsList();
    ul.innerHTML = '';
    const frag = document.createDocumentFragment();
    let total = 0;

    items.forEach((it, idx) => {
        const li = document.createElement('li');
        li.className = 'list-group-item d-flex justify-content-between align-items-center';
        li.innerHTML = `<div><strong>${escapeHtml(it.productName)}</strong><div class="text-muted">Qty: ${it.quantity} • $${it.unitPrice.toFixed(2)}</div></div><div><button class="btn btn-sm btn-outline-danger" data-remove="${idx}">Remove</button></div>`;
        frag.appendChild(li);
        total += it.quantity * it.unitPrice;
    });

    ul.appendChild(frag);
    dom.orderTotal().innerText = `$${total.toFixed(2)}`;
}

function addItem() {
    const sel = parseProductValue(dom.productSelect().value);
    const qty = Math.max(1, parseInt(dom.qty().value) || 1);
    const existing = items.find(i => i.productName === sel.name);
    if (existing) existing.quantity += qty;
    else items.push({ productName: sel.name, quantity: qty, unitPrice: sel.price });
    renderItems();
}

// expose for inline handlers in generated HTML
window.removeItem = function (i) { items.splice(i, 1); renderItems(); };
function clearItems() { items = []; renderItems(); }

async function createOrder(e) {
    e.preventDefault();
    if (items.length === 0) { alert('Add items'); return; }
    let email = dom.email().value.trim();
    // If user is authenticated customer and email not manually entered, use their account email
    if ((!email || email.length === 0) && currentUser && currentUser.role && currentUser.role.toLowerCase() === 'customer') {
        email = currentUser.email || currentUser.emailAddress || '';
    }

    if (!email) { alert('Enter email'); return; }

    const payload = { email, items };
    try {
        const res = await apiFetch('/api/orders', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        if (!res.ok) throw new Error('Failed to create order');
        const data = await res.json().catch(() => null);
        alert('Created order: ' + (data?.id || JSON.stringify(data)));
        clearItems();
        dom.email().value = '';
        await loadOrders();
    } catch (err) { console.error(err); alert('Failed to create order'); }
}

async function loadOrders() {
    const container = dom.ordersContainer();
    container.innerHTML = '<div class="text-muted">Loading...</div>';
    try {
        const res = await apiFetch('/api/orders');
        if (!res.ok) throw new Error('api error: ' + res.status);
        const data = await res.json();
        allOrders = data || [];
        ensureStatusFilter();
        renderOrders();
    } catch (e) {
        console.error(e);
        container.innerHTML = '<div class="text-danger">Failed to load orders</div>';
    }
}

function ensureStatusFilter() {
    const sel = dom.filterStatus();
    if (!sel) return;
    if (sel.options.length > 1) return;
    Object.entries(statuses).forEach(([k, v]) => {
        const opt = document.createElement('option'); opt.value = String(k); opt.text = v; sel.appendChild(opt);
    });
}

function getStatusText(o) {
    if (o == null) return 'Unknown';
    if (typeof o === 'number') return statuses[o] ?? String(o);
    if (typeof o === 'string') {
        const n = parseInt(o);
        if (!isNaN(n)) return statuses[n] ?? o;
        return o;
    }
    return String(o);
}

function renderOrders() {
    const container = dom.ordersContainer();
    const sel = dom.filterStatus();
    const search = (dom.searchInput()?.value || '').trim().toLowerCase();
    const statusFilter = sel?.value || '';

    const getOrderStatusId = (o) => {
        const s = o.status ?? o.statusId ?? null;
        if (typeof s === 'number') return s;
        if (typeof s === 'string') {
            const n = parseInt(s);
            if (!isNaN(n)) return n;
            for (const [k, v] of Object.entries(statuses)) {
                if (String(v).toLowerCase() === s.toLowerCase()) return Number(k);
            }
        }
        return null;
    };

    const filtered = allOrders.filter(o => {
        if (statusFilter) {
            const orderStatusId = getOrderStatusId(o);
            if (orderStatusId === null || String(orderStatusId) !== statusFilter) return false;
        }
        if (search) {
            const email = (o.email || '').toLowerCase();
            const itemsText = (o.items || []).map(it => (it.productName || '').toLowerCase()).join(' ');
            if (email.indexOf(search) === -1 && itemsText.indexOf(search) === -1) return false;
        }
        return true;
    });

    if (filtered.length === 0) { container.innerHTML = '<div class="text-muted">No orders</div>'; return; }

    const role = (currentUser && (currentUser.role || '').toLowerCase()) || '';

    const html = filtered.map(o => {
        const status = getStatusText(o.status ?? o.statusId);
        const itemsText = (o.items || []).map(it => escapeHtml(it.productName)).join(', ');

        const orderStatusId = getOrderStatusId(o);

        // Actions per role
        const actions = [];

        // Admin can cancel any pending order and also keep cancel button (existing behavior)
        if (role === 'admin') {
            if (orderStatusId === 1) actions.push(`<button class="btn btn-sm btn-outline-danger me-1" data-cancel="${o.id}">Cancel</button>`);
        }

        // Customer can cancel their pending orders
        if (role === 'customer') {
            if (orderStatusId === 1) actions.push(`<button class="btn btn-sm btn-outline-danger me-1" data-cancel="${o.id}">Cancel</button>`);
        }

        // Shipping admin (and Admin) can mark Processing -> Shipped
        if (role === 'shippingadmin' || role === 'shipping-admin' || role === 'admin') {
            if (orderStatusId === 2) actions.push(`<button class="btn btn-sm btn-primary me-1" data-ship="${o.id}">Mark Shipped</button>`);
        }

        // Delivery admin (and Admin) can mark Shipped -> Delivered
        if (role === 'deliveryadmin' || role === 'delivery-admin' || role === 'admin') {
            if (orderStatusId === 3) actions.push(`<button class="btn btn-sm btn-success me-1" data-deliver="${o.id}">Mark Delivered</button>`);
        }

        return `<div class="list-group-item d-flex justify-content-between align-items-center"><div><div><strong>${escapeHtml(o.email)}</strong> <small class="text-muted">${escapeHtml(status)}</small></div><div class="text-muted">${itemsText}</div></div><div>${actions.join('')}</div></div>`;
    }).join('');

    container.innerHTML = html;

    // delegate cancel buttons
    container.querySelectorAll('[data-cancel]').forEach(btn => btn.addEventListener('click', () => cancelOrder(btn.getAttribute('data-cancel'))));
    // delegate ship buttons
    container.querySelectorAll('[data-ship]').forEach(btn => btn.addEventListener('click', () => shipOrder(btn.getAttribute('data-ship'))));
    // delegate deliver buttons
    container.querySelectorAll('[data-deliver]').forEach(btn => btn.addEventListener('click', () => deliverOrder(btn.getAttribute('data-deliver'))));
}

async function cancelOrder(id) {
    if (!confirm('Cancel order?')) return;
    try {
        const res = await apiFetch('/api/orders/' + id, { method: 'DELETE' });
        if (res.ok) await loadOrders(); else alert('Cancel failed');
    } catch (e) { console.error(e); alert('Cancel failed'); }
}

async function shipOrder(id) {
    if (!confirm('Mark order as shipped?')) return;
    try {
        const res = await apiFetch('/api/orders/' + id + '/ship', { method: 'POST' });
        if (res.ok) await loadOrders(); else {
            const txt = await res.text().catch(() => 'Failed');
            alert('Mark shipped failed: ' + txt);
        }
    } catch (e) { console.error(e); alert('Mark shipped failed'); }
}

async function deliverOrder(id) {
    if (!confirm('Mark order as delivered?')) return;
    try {
        const res = await apiFetch('/api/orders/' + id + '/deliver', { method: 'POST' });
        if (res.ok) await loadOrders(); else {
            const txt = await res.text().catch(() => 'Failed');
            alert('Mark delivered failed: ' + txt);
        }
    } catch (e) { console.error(e); alert('Mark delivered failed'); }
}

// Event wiring
dom.addBtn()?.addEventListener('click', addItem);
dom.clearBtn()?.addEventListener('click', clearItems);
dom.orderForm()?.addEventListener('submit', createOrder);
dom.refreshBtn()?.addEventListener('click', loadOrders);
dom.filterStatus()?.addEventListener('change', () => renderOrders());

// Debounced search
let searchTimer = null;
dom.searchInput()?.addEventListener('input', () => {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(() => { renderOrders(); }, 250);
});

async function loadProducts() {
    try {
        const res = await apiFetch('/api/products');
        if (!res.ok) return;
        const products = await res.json();
        const sel = dom.productSelect();
        sel.innerHTML = products.map(p => `<option value="${escapeHtml(p.name)}|${Number(p.price)}">${escapeHtml(p.name)} — $${Number(p.price).toFixed(2)}</option>`).join('');
    } catch (e) { console.error('Failed to load products', e); }
}

// Init / auth UI
async function updateAuthUI() {
    const btnLogin = dom.loginBtn();
    const btnLogout = dom.logoutBtn();
    const roleSpan = dom.userRole();
    try {
        const res = await apiFetch('/api/auth/whoami');
        if (!res.ok) { btnLogin.classList.remove('d-none'); btnLogout.classList.add('d-none'); roleSpan.innerText = ''; return false; }
        const u = await res.json();
        currentUser = u;
        btnLogin.classList.add('d-none'); btnLogout.classList.remove('d-none'); roleSpan.innerText = u.role || '';

        // Prefill email for customers and make editable toggle
        try {
            const emailInput = dom.email();
            if (emailInput && currentUser && (currentUser.role || '').toLowerCase() === 'customer') {
                emailInput.value = currentUser.email || '';
                emailInput.readOnly = true;

                // add edit button if not present
                if (!document.getElementById('editEmailBtn')) {
                    const btn = document.createElement('button');
                    btn.id = 'editEmailBtn';
                    btn.type = 'button';
                    btn.className = 'btn btn-sm btn-outline-secondary ms-2';
                    btn.innerText = 'Edit';
                    btn.addEventListener('click', () => { emailInput.readOnly = false; emailInput.focus(); });
                    emailInput.parentNode.appendChild(btn);
                }
            }
        } catch (e) { /* ignore UI prefilling errors */ }
        await loadOrders();
        return true;
    } catch (e) { btnLogin.classList.remove('d-none'); btnLogout.classList.add('d-none'); roleSpan.innerText = ''; return false; }
}

dom.logoutBtn()?.addEventListener('click', async () => {
    await apiFetch('/api/auth/logout', { method: 'POST' });
    try { sessionStorage.removeItem('basicAuth'); } catch (e) { }
    await updateAuthUI();
    try { window.location.replace('/login.html'); } catch (e) { window.location.href = '/login.html'; }
});

// Initialize app
async function initApp() {
    const authenticated = await updateAuthUI();
    const path = window.location.pathname || '/';
    if (!authenticated && (path === '/' || path.endsWith('/index.html'))) {
        window.location.href = '/login.html';
        return;
    }

    renderItems();
    await loadProducts();
    await loadOrders();

    try {
        const params = new URLSearchParams(window.location.search);
        const v = params.get('view');
        if (v === 'orders') switchTab('orders');
    } catch (e) { /* ignore */ }
}

initApp();

// preserve backward compatibility for any other code expecting updateAuthUI to exist globally
window.updateAuthUI = updateAuthUI;
