// Own Qt widgets using public Qt 5.15 APIs. No native business method/offsets.
#include <QtCore/qglobal.h>
// Vendor Qt omits some out-of-line exports of inline geometry accessors.
// Value types have no shared data ownership. Emit their public header inlines
// locally; retain dllimport for all QObject/widget classes and shared data.
#pragma push_macro("Q_CORE_EXPORT")
#undef Q_CORE_EXPORT
#define Q_CORE_EXPORT
#include <QtCore/qpoint.h>
#include <QtCore/qsize.h>
#include <QtCore/qrect.h>
#include <QtCore/qmargins.h>
#pragma pop_macro("Q_CORE_EXPORT")
#include <QApplication>
#include <QPushButton>
#include <QRadioButton>
#include <QBoxLayout>
#include <QGridLayout>
#include <QPointer>
#include <QThread>
#include <QLibraryInfo>
#include <QVersionNumber>
#include <QEvent>
#include <QWheelEvent>
#include <QStyle>
#include <QStyleOptionButton>
#include <QPainter>
#include <Windows.h>
#include <atomic>
#include <filesystem>
#include <string>

namespace {
HMODULE module;
HHOOK messageHook;
UINT_PTR timer;
std::atomic<bool> enabled{false};
std::atomic<bool> installed{false};
std::atomic<bool> removing{false};
DWORD uiThread;
// The vendor library retains the wheel-event vtable slot although it does not
// export QWidget::wheelEvent. Removing the header feature shifts every later
// slot (including painting/click handling). Override the public virtual instead.
class OptionsButton final:public QPushButton {
public:
    OptionsButton(const QString& text,QWidget* parent):QPushButton(text,parent){}
protected:
    void wheelEvent(QWheelEvent* event) override {event->ignore();}
};
class OptionsRadioButton final:public QRadioButton {
    QPointer<QRadioButton> native;
public:
    OptionsRadioButton(const QString& text,QWidget* parent,QRadioButton* source=nullptr):QRadioButton(text,parent),native(source) {setAutoExclusive(false);setCheckable(false);}
    QSize sizeHint() const override {
        if(!native)return QRadioButton::sizeHint();
        auto size=native->sizeHint();
        const auto metrics=native->fontMetrics();
        size.setWidth(qMax(0,size.width()+metrics.boundingRect(text()).width()-metrics.boundingRect(native->text()).width()));
        return size;
    }
protected:
    void wheelEvent(QWheelEvent* event) override {event->ignore();}
    void paintEvent(QPaintEvent* event) override {
        if(!native){QRadioButton::paintEvent(event);return;}
        // Resolve styling against the real native action, not a new widget's
        // objectName/class/property ancestry. QSS and proxy styles may use all
        // of these (and can be assigned after our widget is created).
        // Draw a live native control, not a captured image. Only text/rect and
        // interaction state come from our independent, non-exclusive action.
        QStyleOptionButton option;initStyleOption(&option);
        option.styleObject=native;option.palette=native->palette();option.fontMetrics=native->fontMetrics();
        option.icon=native->icon();option.iconSize=native->iconSize();
        option.state&=~(QStyle::State_On|QStyle::State_Off);
        option.state|=native->isChecked()?QStyle::State_On:QStyle::State_Off;
        QPainter painter(this);
        native->style()->drawControl(QStyle::CE_RadioButton,&option,&painter,native);
    }
};
QString buttonStyle(QWidget* original) {
    // Native QSS can target #btnUninstallGame in an ancestor stylesheet rather
    // than in original->styleSheet(). Retarget only our local copy; otherwise
    // the new object name loses native text/background/hover styling.
    auto* app=qobject_cast<QApplication*>(QCoreApplication::instance());
    auto style=app?app->styleSheet():QString();
    QList<QWidget*> ancestors;
    for(auto* node=original;node;node=node->parentWidget())ancestors.prepend(node);
    for(auto* node:ancestors)style+=QLatin1Char('\n')+node->styleSheet();
    style.replace(QStringLiteral("#btnUninstallGame"),QStringLiteral("#zmlUninstallButton"));
    return style;
}
void buttonIcon(QWidget* original,QAbstractButton* own) {
    auto* source=qobject_cast<QAbstractButton*>(original);
    auto icon=source?source->icon():QIcon();
    own->setIcon(icon);
    own->setIconSize(source?source->iconSize():QSize(original->style()->pixelMetric(QStyle::PM_SmallIconSize),original->style()->pixelMetric(QStyle::PM_SmallIconSize)));
}
QAbstractButton* createButton(QWidget* original,QWidget* parent) {
    // Actual CGameResource uses QRadioButton with a skinned ::indicator. A
    // QPushButton + Windows trash icon cannot reproduce that native renderer.
    if(auto* radio=qobject_cast<QRadioButton*>(original))return new OptionsRadioButton(QStringLiteral("卸载 ZML"),parent,radio);
    if(qobject_cast<QPushButton*>(original))return new OptionsButton(QStringLiteral("卸载 ZML"),parent);
    return nullptr; // Unknown renderer: do not guess its visual/interaction ABI.
}
#ifdef ZML_OPTIONS_TEST
unsigned testUninstallRequests;
#endif
class ButtonPlacement final:public QObject {
    QPointer<QWidget> original;
    QPointer<QAbstractButton> own;
public:
    ButtonPlacement(QWidget* source,QAbstractButton* button):QObject(button),original(source),own(button) {
        source->installEventFilter(this);source->parentWidget()->installEventFilter(this);update();
    }
    void update() {
        if(!original || !own || !original->parentWidget())return;
        auto* parent=original->parentWidget();auto rect=original->geometry();
        // Native absolute-positioned forms: derive placement from the actual control,
        // never fixed desktop pixels. Do not overlap another native control.
        int width=qMax(rect.width(),own->sizeHint().width());
        auto fits=[&](const QRect& candidate){
            if(!parent->rect().contains(candidate))return false;
            for(auto* child:parent->findChildren<QWidget*>(QString(),Qt::FindDirectChildrenOnly))
                if(child!=own && child!=original && !child->isHidden() && child->geometry().intersects(candidate))return false;
            return true;
        };
        QRect next(rect.right()+9,rect.y(),width,rect.height());
        if(!fits(next))next=QRect(rect.left()-width-8,rect.y(),width,rect.height());
        if(!fits(next)){own->hide();return;}
        own->setGeometry(next);own->setVisible(enabled.load() && !original->isHidden());
    }
    bool eventFilter(QObject* source,QEvent* event) override {
        if(event->type()==QEvent::Resize || event->type()==QEvent::Move || event->type()==QEvent::Show || event->type()==QEvent::Hide)update();
        return QObject::eventFilter(source,event);
    }
};
void uninstall() {
#ifdef ZML_OPTIONS_TEST
    ++testUninstallRequests;
    return; // Fixtures never launch an uninstaller or close real processes.
#endif
    if(removing.exchange(true))return;
    wchar_t dll[32768],tmp[32768];
    if(!GetModuleFileNameW(module,dll,32768) || !GetTempPathW(32768,tmp)){removing=false;return;}
    auto zml=std::filesystem::path(dll).parent_path();
    auto work=std::filesystem::path(tmp)/(L"ZML-uninstall-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64()));
    if(!CreateDirectoryW(work.c_str(),nullptr)){removing=false;return;}
    auto exe=work/L"ZMLUninstall.exe";
    if(!CopyFileW((zml/L"ZMLUninstall.exe").c_str(),exe.c_str(),TRUE)){removing=false;return;}
    std::wstring command=L"\""+exe.wstring()+L"\" --uninstall-running \""+zml.parent_path().wstring()+L"\"";
    STARTUPINFOW start{};start.cb=sizeof(start);PROCESS_INFORMATION child{};
    if(!CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,0,nullptr,work.c_str(),&start,&child)) {removing=false;return;}
    CloseHandle(child.hThread);CloseHandle(child.hProcess);
    // The helper owns confirmation and shutdown. A cancel permits a later retry.
    removing=false;
}
void scan() {
    if(QThread::currentThreadId()!=reinterpret_cast<Qt::HANDLE>(static_cast<uintptr_t>(uiThread)))return;
    for(auto* top:QApplication::allWidgets()) {
        if(!top || (QString::fromLatin1(top->metaObject()->className())!=QStringLiteral("CGameSettingDlg") && top->objectName()!=QStringLiteral("game_setting_dlg")))continue;
        auto originals=top->findChildren<QWidget*>(QStringLiteral("btnUninstallGame"));
        if(originals.size()!=1)continue;
        auto* original=originals.front();auto* parent=original->parentWidget();if(!parent)continue;
        auto* own=parent->findChild<QAbstractButton*>(QStringLiteral("zmlUninstallButton"),Qt::FindDirectChildrenOnly);
        if(!enabled.load()) {if(own)own->hide();continue;}
        if(own){
            if(own->font()!=original->font())own->setFont(original->font());
            if(own->palette()!=original->palette())own->setPalette(original->palette());
            if(parent->layout())own->show();
            else for(auto* child:own->children())if(auto* placement=dynamic_cast<ButtonPlacement*>(child))placement->update();
            continue;
        }
        auto* layout=parent->layout();
        bool absolute=!layout;
        if(layout && layout->indexOf(original)<0)continue;
        auto* box=qobject_cast<QBoxLayout*>(layout);auto* grid=qobject_cast<QGridLayout*>(layout);
        if(!box && !grid && !absolute)continue;
        int row=0,column=0,rows=0,columns=0;
        if(grid){grid->getItemPosition(grid->indexOf(original),&row,&column,&rows,&columns);if(rows!=1 || columns!=1 || grid->itemAtPosition(row,column+1))continue;}
        own=createButton(original,parent);if(!own)continue;
        own->setObjectName(QStringLiteral("zmlUninstallButton"));
        own->setFont(original->font());own->setPalette(original->palette());
        // Ancestors may use valid widget-local bare declarations. Concatenating
        // these with selector blocks makes an invalid stylesheet. The native
        // radio action already resolves its entire cascade through the source.
        if(!qobject_cast<QRadioButton*>(original))own->setStyleSheet(buttonStyle(original));
        buttonIcon(original,own);
        own->setMinimumHeight(original->minimumHeight());own->setSizePolicy(original->sizePolicy());
        own->setToolTip(QStringLiteral("关闭此游戏与启动器，恢复官方文件并卸载 ZML；保留模组与私人配置"));
        QObject::connect(own,&QAbstractButton::clicked,own,[]{uninstall();});
        if(box)box->insertWidget(box->indexOf(original)+1,own);else if(grid)grid->addWidget(own,row,column+1);
        else new ButtonPlacement(original,own);
    }
}
void CALLBACK tick(HWND,UINT,UINT_PTR,DWORD) {try{scan();}catch(...) {/* Optional UI must not unwind into the launcher message loop. */}}
LRESULT CALLBACK message(int code,WPARAM w,LPARAM l) {
    if(code>=0 && GetCurrentThreadId()==uiThread && !timer)timer=SetTimer(nullptr,0,500,tick);
    return CallNextHookEx(messageHook,code,w,l);
}
BOOL CALLBACK window(HWND w,LPARAM) {
    DWORD pid=0;auto thread=GetWindowThreadProcessId(w,&pid);
    if(pid==GetCurrentProcessId() && IsWindowVisible(w)) {if(uiThread && uiThread!=thread){uiThread=0;return FALSE;}uiThread=thread;}
    return TRUE;
}
}
extern "C" __declspec(dllexport) DWORD WINAPI ZML_SetOptionsEnabled(void* value) {
    // Remote callers pass only 0/1 as the parameter value; no caller-supplied paths.
    if(reinterpret_cast<uintptr_t>(value)>1)return ERROR_INVALID_PARAMETER;
    auto version=QLibraryInfo::version();
    if(version.majorVersion()!=5 || version.minorVersion()!=15 || version.microVersion()!=8)return ERROR_REVISION_MISMATCH;
    enabled=value!=nullptr;
    if(!installed.exchange(true)) {
        uiThread=0;
        EnumWindows(window,0);
        if(!uiThread || !QApplication::instance()){installed=false;return ERROR_NOT_READY;}
        messageHook=SetWindowsHookExW(WH_GETMESSAGE,message,module,uiThread);
        if(!messageHook){installed=false;return GetLastError();}
        PostThreadMessageW(uiThread,WM_NULL,0,0);
    }
    return ERROR_SUCCESS;
}
BOOL WINAPI DllMain(HINSTANCE m,DWORD reason,LPVOID) {if(reason==DLL_PROCESS_ATTACH){module=m;DisableThreadLibraryCalls(m);}return TRUE;}
