export interface SideNavItemModel { readonly id: string; readonly label: string; readonly href?: string; readonly disabled?: boolean; readonly children?: readonly SideNavItemModel[] }
export interface SideNavGroup { readonly id: string; readonly label?: string; readonly items: readonly SideNavItemModel[] }

export function createSideNavGroup(id: string, items: readonly SideNavItemModel[], label?: string): SideNavGroup {
  if (!id.trim() || items.some(item => !item.id.trim())) throw new Error('invalid-side-nav-identity')
  return { id, label, items: items.map(item => ({ ...item, children: item.children?.map(child => ({ ...child })) })) }
}
