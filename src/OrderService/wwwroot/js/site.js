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
            const idText = String(o.id || o.orderId || '').toLowerCase();
            if (email.indexOf(search) === -1 && itemsText.indexOf(search) === -1 && idText.indexOf(search) === -1) return false;
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

        // show short order id for quick reference
        const shortId = String(o.id || o.orderId || '').slice(0, 8);
        return `<div class="list-group-item d-flex justify-content-between align-items-center" data-order-id="${o.id}">
                    <div>
                        <div><strong>${escapeHtml(o.email)}</strong> <small class="text-muted">${escapeHtml(status)}</small></div>
                        <div class="text-muted">${itemsText}</div>
                        <div class="text-muted small">OrderId: ${escapeHtml(shortId)}</div>
                    </div>
                    <div>${actions.join('')} <button class="btn btn-sm btn-outline-info ms-2" data-view="${o.id}">View</button></div>
                </div>`;
    }).join('');

    container.innerHTML = html;

    // delegate cancel buttons
    container.querySelectorAll('[data-cancel]').forEach(btn => btn.addEventListener('click', () => cancelOrder(btn.getAttribute('data-cancel'), btn)));
    // delegate ship buttons
    container.querySelectorAll('[data-ship]').forEach(btn => btn.addEventListener('click', () => shipOrder(btn.getAttribute('data-ship'), btn)));
    // delegate deliver buttons - pass the button element so handler can update UI optimistically
    container.querySelectorAll('[data-deliver]').forEach(btn => btn.addEventListener('click', () => deliverOrder(btn.getAttribute('data-deliver'), btn)));

    // delegate view buttons (install directly so they always work after render)
    container.querySelectorAll('[data-view]').forEach(btn => btn.addEventListener('click', () => showOrderDetails(btn.getAttribute('data-view'))));

}

async function refreshOrder(id) {
    try {
        const res = await apiFetch('/api/orders/' + id);
        if (!res.ok) return null;
        const order = await res.json();
        // replace in allOrders
        const idx = allOrders.findIndex(o => String(o.id) === String(id));
        if (idx >= 0) {
            allOrders[idx] = order;
        } else {
            // if not present, add
            allOrders.push(order);
        }

        // re-render orders to reflect updated status
        renderOrders();
        return order;
    } catch (e) {
        console.error('Failed to refresh order', e);
        return null;
    }
}

async function showOrderDetails(id) {
    try {
        const res = await apiFetch('/api/orders/' + id);
        if (!res.ok) return;
        const o = await res.json();
        const body = document.getElementById('orderDetailsBody');
        const itemsHtml = (o.items || []).map(it => `<li>${escapeHtml(it.productName)} — ${it.quantity} × $${Number(it.unitPrice).toFixed(2)}</li>`).join('');
        body.innerHTML = `
            <div><strong>Order ID:</strong> ${escapeHtml(String(o.id))}</div>
            <div><strong>Email:</strong> ${escapeHtml(o.email || '')}</div>
            <div><strong>Status:</strong> ${escapeHtml(getStatusText(o.status || o.statusId))}</div>
            <div><strong>Created:</strong> ${escapeHtml(o.createdAtUtc || '')}</div>
            <hr />
            <h6>Items</h6>
            <ul>${itemsHtml}</ul>
            <div class="mt-2"><strong>Total:</strong> $${Number(o.totalAmount || o.TotalAmount || 0).toFixed(2)}</div>
        `;
        // Show modal using getOrCreateInstance if available to avoid duplicate instances
        const modalEl = document.getElementById('orderDetailsModal');
        const modal = (bootstrap.Modal.getOrCreateInstance && bootstrap.Modal.getOrCreateInstance(modalEl)) || new bootstrap.Modal(modalEl);
        modal.show();
    } catch (e) { console.error('Failed to load order details', e); }
}

async function cancelOrder(id, btn) {
    if (!confirm('Cancel order?')) return;

    const item = btn ? btn.closest('.list-group-item') : null;
    // disable all buttons for this order
    if (item) item.querySelectorAll('button').forEach(b => b.disabled = true);
    // show spinner on clicked button
    if (btn) {
        btn.dataset.origText = btn.innerText;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Cancelling';
    }
    // Immediately hide details modal (if open) to avoid UI being blocked while request runs
    try {
        const modalEl = document.getElementById('orderDetailsModal');
        if (modalEl) {
            const inst = (bootstrap.Modal.getOrCreateInstance && bootstrap.Modal.getOrCreateInstance(modalEl)) || (bootstrap.Modal.getInstance && bootstrap.Modal.getInstance(modalEl));
            try { inst && inst.hide(); } catch (e) { }
        }
        document.querySelectorAll('.modal-backdrop').forEach(n => n.remove());
        document.body.classList.remove('modal-open');
    } catch (e) { /* ignore modal cleanup errors */ }

    try {
        const res = await apiFetch('/api/orders/' + id, { method: 'DELETE' });
        if (res.ok) {
            await loadOrders();
        } else {
            const txt = await res.text().catch(() => 'Failed');
            alert('Cancel failed: ' + txt);
        }
    } catch (e) {
        console.error(e);
        alert('Cancel failed');
    } finally {
        // re-enable UI
        try { if (item) item.querySelectorAll('button').forEach(b => b.disabled = false); } catch {}
        try { if (btn) btn.innerText = btn.dataset.origText || 'Cancel'; } catch {}
    }
}

async function shipOrder(id, btn) {
    if (!confirm('Mark order as shipped?')) return;

    const item = btn ? btn.closest('.list-group-item') : null;
    if (item) item.querySelectorAll('button').forEach(b => b.disabled = true);
    if (btn) { btn.dataset.origText = btn.innerText; btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Shipping'; }

    try {
        const res = await apiFetch('/api/orders/' + id + '/ship', { method: 'POST' });
        if (res.ok) {
            await refreshOrder(id);
        } else {
            const txt = await res.text().catch(() => 'Failed');
            alert('Mark shipped failed: ' + txt);
            if (item) item.querySelectorAll('button').forEach(b => b.disabled = false);
            if (btn) btn.innerText = btn.dataset.origText || 'Mark Shipped';
        }
    } catch (e) { console.error(e); alert('Mark shipped failed'); if (item) item.querySelectorAll('button').forEach(b => b.disabled = false); if (btn) btn.innerText = btn.dataset.origText || 'Mark Shipped'; }
}

async function deliverOrder(id, btn) {
    if (!confirm('Mark order as delivered?')) return;

    const item = btn ? btn.closest('.list-group-item') : null;
    if (item) item.querySelectorAll('button').forEach(b => b.disabled = true);
    if (btn) { btn.dataset.origText = btn.innerText; btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Delivering'; }

    try {
        const res = await apiFetch('/api/orders/' + id + '/deliver', { method: 'POST' });
        if (res.ok) {
            await refreshOrder(id);
        } else {
            const txt = await res.text().catch(() => 'Failed');
            alert('Mark delivered failed: ' + txt);
            if (item) item.querySelectorAll('button').forEach(b => b.disabled = false);
            if (btn) btn.innerText = btn.dataset.origText || 'Mark Delivered';
        }
    } catch (e) {
        console.error(e);
        alert('Mark delivered failed');
        if (item) item.querySelectorAll('button').forEach(b => b.disabled = false);
        if (btn) btn.innerText = btn.dataset.origText || 'Mark Delivered';
    }
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

    // Install a single delegated click handler for view buttons so it works
    // even when the list is re-rendered. Uses a global flag to avoid double registration.
    if (!window._viewHandlerInstalled) {
        document.addEventListener('click', (ev) => {
            try {
                const btn = ev.target.closest('[data-view]');
                if (btn) {
                    const id = btn.getAttribute('data-view');
                    if (id) showOrderDetails(id);
                }
            } catch (e) { /* ignore */ }
        });
        window._viewHandlerInstalled = true;
    }

    // Start periodic polling to pick up background status changes (orders processed by background workers)
    // Poll only visible orders to avoid unnecessary load.
    setInterval(() => {
        try {
            const container = dom.ordersContainer();
            if (!container) return;
            const ids = Array.from(container.querySelectorAll('[data-order-id]')).map(el => el.getAttribute('data-order-id'));
            ids.forEach(id => {
                if (id) refreshOrder(id);
            });
        } catch (e) { /* ignore polling errors */ }
    }, 600_000);

    try {
        const params = new URLSearchParams(window.location.search);
        const v = params.get('view');
        if (v === 'orders') switchTab('orders');
    } catch (e) { /* ignore */ }
}

initApp();

// preserve backward compatibility for any other code expecting updateAuthUI to exist globally
window.updateAuthUI = updateAuthUI;
