import React from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { Building2, Database, Gauge, Layers, ShieldCheck, SlidersHorizontal, Lock } from 'lucide-react';
import type { ManagementContextDto } from '../types';

const MODULE_LABELS: Record<string, { vi: string; en: string }> = {
  M00: { vi: 'Không gian quản trị (M00)', en: 'Management Foundation (M00)' },
  M01: { vi: 'Điều hành thông minh (M01)', en: 'Executive Intelligence (M01)' },
  M02: { vi: 'Phân tích & Báo cáo BI (M02)', en: 'Business Intelligence (M02)' },
  M03: { vi: 'Quy trình & Phê duyệt (M03)', en: 'Workflow & Approval (M03)' },
  M04: { vi: 'Tổ chức & Nhân sự (M04)', en: 'Organization & People (M04)' },
  M05: { vi: 'Rủi ro & Quản trị (M05)', en: 'Risk & Governance (M05)' },
  M06: { vi: 'Tuân thủ pháp lý (M06)', en: 'Compliance Intelligence (M06)' },
  M07: { vi: 'Báo cáo HĐQT (M07)', en: 'Board Reporting (M07)' },
  M08: { vi: 'Mục tiêu & Hiệu suất (M08)', en: 'Resource & Performance (M08)' },
  M09: { vi: 'Trợ lý AI Điều hành (M09)', en: 'AI Executive Intelligence (M09)' },
  M10: { vi: 'Dữ liệu & Tích hợp (M10)', en: 'Data & Integration Hub (M10)' },
};

export function ForbiddenBanner() {
  return (
    <div className="alert-danger" role="alert">
      Vui lòng liên hệ Admin để xem chi tiết
    </div>
  );
}

interface AppShellProps {
  context: ManagementContextDto;
  onLogout: () => void;
}

export const AppShell: React.FC<AppShellProps> = ({ context, onLogout }) => {
  const isVi = context.preferences.locale.toLowerCase().startsWith('vi');
  const lang = isVi ? 'vi' : 'en';
  const hasAudit = context.permissions.includes('m.audit.read');
  const hasKpi = context.permissions.includes('m.kpi.read');

  return (
    <div className="shell">
      <aside className="sidebar" aria-label="NEXUS M Navigation">
        <div className="brand">
          <span className="brand-badge">M</span>
          <div>
            <div className="brand-title">NEXUS M</div>
            <div className="brand-sub">{context.activeWorkspace.name}</div>
          </div>
        </div>

        <nav className="nav-group">
          <div className="nav-label">{isVi ? 'NỀN TẢNG QUẢN TRỊ' : 'FOUNDATION'}</div>
          <NavLink to="/workspace" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Building2 size={16} />
            <span>{isVi ? 'Không gian làm việc' : 'Workspaces'}</span>
          </NavLink>
          <NavLink to="/modules" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Layers size={16} />
            <span>{isVi ? 'Danh mục phân hệ' : 'Module Registry'}</span>
          </NavLink>
          <NavLink to="/preferences" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <SlidersHorizontal size={16} />
            <span>{isVi ? 'Tùy chọn cá nhân' : 'Preferences'}</span>
          </NavLink>
          {hasKpi && (
            <NavLink to="/kpis" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
              <Gauge size={16} />
              <span>{isVi ? 'Danh mục KPI' : 'KPI Catalog'}</span>
            </NavLink>
          )}
          {hasAudit && (
            <NavLink to="/audit" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
              <ShieldCheck size={16} />
              <span>{isVi ? 'Nhật ký quản trị' : 'Business Audit'}</span>
            </NavLink>
          )}
        </nav>

        <nav className="nav-group">
          <div className="nav-label">{isVi ? 'PHÂN HỆ THEO QUYỀN' : 'ENTITLED MODULES'}</div>
          {context.modules
            .filter((m) => m.code !== 'M00')
            .map((mod) => {
              const label = MODULE_LABELS[mod.code]?.[lang] ?? mod.code;
              if (mod.isAccessible) {
                return (
                  <NavLink
                    key={mod.code}
                    to={mod.route}
                    className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
                  >
                    <Database size={14} />
                    <span>{label}</span>
                  </NavLink>
                );
              }
              return (
                <div
                  key={mod.code}
                  className="nav-item disabled"
                  title={isVi ? 'Chưa kích hoạt hoặc chưa đủ quyền' : 'Not entitled or planned'}
                >
                  <Lock size={14} />
                  <span>{label}</span>
                </div>
              );
            })}
        </nav>
      </aside>

      <div className="main-area">
        <header className="topbar">
          <div className="topbar-left">
            <span className="pill">{context.activeWorkspace.code}</span>
            <span className="pill subtle">{context.activeWorkspace.defaultCurrency}</span>
            <span className="pill subtle">
              {context.orgScope.hasUnrestrictedTenantScope
                ? isVi ? 'Toàn Tenant' : 'Full Tenant Scope'
                : `${context.orgScope.organizationUnitIds.length} Org Units`}
            </span>
          </div>
          <div className="topbar-right">
            <span className="user-email">{context.email}</span>
            <button type="button" className="btn-secondary" onClick={onLogout}>
              {isVi ? 'Đăng xuất' : 'Sign out'}
            </button>
          </div>
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
