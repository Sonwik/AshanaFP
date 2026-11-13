# User guide

Ashana has three kinds of users. Everyone signs in at **Sign in** (top right). Students can create an account
themselves; cooks and administrators get their role from an administrator.

## Students

### Order

1. **Choose dishes.** Open **Menu**. Search by name or ingredient, filter by category, tick *Vegetarian* or
   *Available now*, or sort by price. Press **Add**; press it again with **+** and **−** to change the quantity.
   * "3 left" means only three portions remain today. "Sold out" dishes cannot be added.
   * Your order stays in the browser if you reload the page or come back later on the same device.
2. **Choose a pickup time.** Press **Choose pickup time**. Each 15-minute slot shows how busy it is:
   *Quiet*, *N left* (busy) or *Full*. The kitchen needs about 10 minutes, so slots that start too soon are
   disabled. A quiet slot means a shorter wait at the counter.
3. **Choose how you will pay** at the counter (card, cash or QR code), add a comment for the kitchen if you like
   ("no onion"), and press **Place order**.

If something changed while you were choosing (a price, the last portions, a dish removed), Ashana fixes your
order and tells you exactly what changed before you confirm.

### Follow your order

After ordering you see a ticket with your **pickup number**. The page updates by itself:

| Status | Meaning |
|---|---|
| Placed | The kitchen has your order. You can still cancel it. |
| Cooking | Your food is being cooked. It can no longer be cancelled in the app. |
| Ready | Come to the counter and say your number. The phone plays a sound and vibrates. |
| Picked up | Enjoy your meal. |
| Not picked up | The food was ready but nobody came within 30 minutes after the slot. |

Press **Notify me when it's ready** on the order page to get a browser notification, even if the tab is in the
background.

**My orders** shows active orders first, then your history. **Order again** puts the same dishes into your cart
with today's prices.

Limits that keep things fair: at most 3 active orders per student, at most 10 portions of one dish and 15
different dishes per order.

## Kitchen staff (role "Cook")

### Kitchen board

**Kitchen → Orders** shows all active orders in three columns: **New**, **Cooking** and **Ready**, sorted by pickup
time. A new order appears by itself with a short highlight and a chime. Sound starts switched off, because browsers only
play sound after a click: press **Sound off** once and it becomes **Sound on**.

Each docket shows the number, the pickup time ("in 10 min", "5 min late"), the student's name, the dishes and the
comment. One big button moves the order to the next step:

**Start cooking** → **Mark ready** → **Hand over** (when the student takes the food).

Late orders are outlined in red. **Cancel** needs a short reason, which the student sees; portions of an order that
was not cooked yet go back on sale automatically.

On a phone the columns become tabs.

### Prep list and stop list

* **Prep list** adds up the portions of every dish for each pickup slot, so you can cook in batches.
* **Stop list** hides a dish from the menu at once, or adds portions when you cooked more.

### Pausing new orders

When the kitchen is overwhelmed, press **Pause new orders** and write a short message. Every open menu shows the
message immediately and nobody can place a new order until you press **Resume orders**. Orders already placed are
not affected.

### TV at the counter

Open **TV screen** on the kitchen board (or go to `/board`) on the TV next to the counter. It shows the numbers
being prepared and, in large figures, the numbers that are ready. It never shows names. It updates by itself and
reconnects automatically if the network drops.

## Administrators

### Dashboard

**Admin → Dashboard** summarises the last 7, 14 or 30 days: revenue (picked-up orders only, because students pay at
the counter), number of orders, average order, average kitchen time, cancellations, uncollected orders and wasted
portions, orders per day, the busiest pickup times and the top dishes. **Export CSV** downloads every order of the
period as a file that opens correctly in Excel.

### Menu

**Admin → Menu** lists every dish in a sortable table. **New dish** or **Edit** opens a form with a live preview of
how the dish will look on the menu. *Portions a day* is the daily stock: it is refilled automatically every morning.
**Archive** removes a dish from the menu but keeps it in the order history.

If two administrators edit the same dish at the same time, the second one to save is told so and can reload the
latest version instead of silently overwriting the first change.

### Settings

**Admin → Settings** controls opening hours, slot length, orders per slot, how many minutes ahead students must
order, when a ready order counts as not picked up and how many active orders a student may have. The panel on the
right shows the slots these settings produce before you save.

### Users

**Admin → Users** lists everyone. Change a role (Student, Cook, Admin) or block an account; a blocked user is signed
out of every open tab within two minutes. You cannot change your own role, and the last active administrator cannot
be removed.

## Accessibility

* Everything works with the keyboard; a "Skip to content" link appears on the first Tab.
* Statuses are written in words, never shown by colour alone.
* Live changes (new status, cart updates) are announced to screen readers.
* The light and the dark theme both meet WCAG AA contrast; the theme follows the device and can be switched on
  the account page.
