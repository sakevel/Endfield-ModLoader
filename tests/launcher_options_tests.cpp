// Hidden widget fixture: no top-level Show(), desktop input or real uninstall.
#define ZML_OPTIONS_TEST
#include "../src/launcher_options.cpp"
#include <QImage>
#include <QPainter>
#include <iostream>
#include <stdexcept>
#include <cstring>
void check(bool ok,const char* name){if(!ok)throw std::runtime_error(name);std::cout<<"PASS "<<name<<'\n';}
class TestWidget final:public QWidget {
public:
    explicit TestWidget(QWidget* parent=nullptr):QWidget(parent){}
protected:
    void wheelEvent(QWheelEvent* event) override {event->ignore();}
};
int main(int argc,char** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    try {
        QApplication app(argc,argv);
        check(ZML_SetOptionsEnabled(nullptr)==ERROR_NOT_READY,"actual native Qt version accepted; missing visible launcher fails closed");
        uiThread=GetCurrentThreadId();enabled=true;
        TestWidget top;top.setObjectName(QStringLiteral("game_setting_dlg"));top.resize(800,400);
        auto* absolute=new TestWidget(&top);absolute->resize(600,300);
        auto* absOriginal=new OptionsButton(QStringLiteral("卸载游戏"),absolute);absOriginal->setObjectName(QStringLiteral("btnUninstallGame"));absOriginal->setGeometry(30,200,120,32);
        absolute->setStyleSheet(QStringLiteral("QPushButton#btnUninstallGame { background: #313131; color: white; border-radius: 8px; }"));
        absOriginal->setIcon(absOriginal->style()->standardIcon(QStyle::SP_TrashIcon,nullptr,absOriginal));absOriginal->setIconSize(QSize(16,16));
        absOriginal->setVisible(true); // Child flag only; hidden top-level is never shown.
        scan();auto* absButton=absolute->findChild<QPushButton*>(QStringLiteral("zmlUninstallButton"));
        check(absButton && absButton->text()==QStringLiteral("卸载 ZML"),"real Qt button text and parent");
        check(absButton->icon().cacheKey()!=0 && absButton->icon().cacheKey()==absOriginal->icon().cacheKey() && absButton->iconSize()==absOriginal->iconSize(),"native uninstall icon and size reused by ZML button");
        absOriginal->setIcon(QIcon());buttonIcon(absOriginal,absButton);
        check(absButton->icon().cacheKey()==0,"no foreign system trash icon added when native button has none");
        absButton->click();check(testUninstallRequests==1,"real native click dispatches exactly once without executing uninstall");
        auto imageSize=absButton->size();QImage paint(imageSize,QImage::Format_ARGB32);paint.fill(Qt::transparent);
        {QPainter painter(&paint);absButton->render(&painter);}
        bool painted=false;auto* pixels=paint.constBits();for(int i=3;i<paint.bytesPerLine()*imageSize.height();i+=4)if(pixels[i])painted=true;
        check(painted,"native button paints into offscreen image without showing a window");
        bool nativeBackground=false,visibleText=false;
        for(int i=0;i<paint.bytesPerLine()*imageSize.height();i+=4) {
            if(pixels[i]==49 && pixels[i+1]==49 && pixels[i+2]==49 && pixels[i+3])nativeBackground=true;
            if(pixels[i]>220 && pixels[i+1]>220 && pixels[i+2]>220 && pixels[i+3])visibleText=true;
        }
        check(nativeBackground && visibleText,"native dark background and contrasting button text actually render");
        check(absButton->styleSheet().contains(QStringLiteral("#zmlUninstallButton")) && absolute->styleSheet().contains(QStringLiteral("#btnUninstallGame")),"ancestor native named-button styling is copied locally without changing native style");
        scan();check(absolute->findChildren<QPushButton*>(QStringLiteral("zmlUninstallButton")).size()==1,"repeated scan is idempotent");
        enabled=false;scan();check(absButton->isHidden(),"non-Endfield theme hides owned button");enabled=true;
        check(absButton && absButton->geometry().left()==158 && absButton->geometry().top()==200,"absolute native form uses live original geometry");
        absOriginal->move(80,180);scan();check(absButton->geometry().left()==208 && absButton->geometry().top()==180,"absolute placement follows native control movement");
        absOriginal->move(500,180);scan();check(absButton->geometry().left()==372,"right-aligned native button receives adjacent left placement");
        absolute->resize(180,300);absOriginal->move(30,180);scan();check(absButton->isHidden(),"overflow is hidden rather than overlapping outside parent");
        delete absolute;
        auto* radioParent=new TestWidget(&top);radioParent->resize(600,300);
        auto* native=new OptionsRadioButton(QStringLiteral("卸载 ZML"),radioParent);native->setObjectName(QStringLiteral("btnUninstallGame"));native->setGeometry(30,200,120,32);native->setVisible(true);
        radioParent->setStyleSheet(QStringLiteral("QRadioButton#btnUninstallGame { color: white; background: transparent; spacing: 6px; } QRadioButton#btnUninstallGame::indicator { width: 16px; height: 16px; border-radius: 2px; background: #ffef00; } QRadioButton#btnUninstallGame:hover { color: #ffef00; } QRadioButton#btnUninstallGame:pressed { color: #313131; } QRadioButton#btnUninstallGame:disabled { color: gray; }"));
        scan();auto* matched=radioParent->findChild<QRadioButton*>(QStringLiteral("zmlUninstallButton"));
        check(matched && matched->icon().cacheKey()==0,"actual native radio renderer reused including skinned indicator without extra icon");
        check(matched->styleSheet().isEmpty() && radioParent->styleSheet().contains(QStringLiteral("QRadioButton#btnUninstallGame::indicator")),"native renderer keeps source style scopes intact rather than flattening ancestor QSS");
        check(!matched->autoExclusive(),"owned action does not join native radio group");
        auto render=[](QWidget* widget){auto size=widget->size();QImage image(size,QImage::Format_ARGB32);image.fill(Qt::transparent);QPainter painter(&image);widget->render(&painter);painter.end();return image;};
        auto left=render(native),right=render(matched);
        check(left.bytesPerLine()==right.bytesPerLine() && std::memcmp(left.constBits(),right.constBits(),left.bytesPerLine()*native->size().height())==0,"same-label native and ZML controls render pixel-identically offscreen");
        auto same=[&](){auto a=render(native),b=render(matched);return a.bytesPerLine()==b.bytesPerLine() && std::memcmp(a.constBits(),b.constBits(),a.bytesPerLine()*native->size().height())==0;};
        native->setAttribute(Qt::WA_UnderMouse,true);matched->setAttribute(Qt::WA_UnderMouse,true);check(same(),"native hover-state rendering matches ZML control");
        native->setDown(true);matched->setDown(true);check(same(),"native pressed-state rendering matches ZML control");
        native->setDown(false);matched->setDown(false);native->setEnabled(false);matched->setEnabled(false);check(same(),"native disabled-state rendering matches ZML control");
        native->setEnabled(true);matched->setEnabled(true);
        // A native skin can depend on properties that the owned action must
        // not copy (for example a business role), and arrive after creation.
        native->setProperty("nativeActionSkin",true);
        native->setStyleSheet(QStringLiteral("QRadioButton[nativeActionSkin=\"true\"] { color: #ffef00; spacing: 9px; } QRadioButton[nativeActionSkin=\"true\"]::indicator { width: 20px; height: 20px; background: #313131; border: 3px solid #ffef00; }"));
        check(same(),"live source renderer preserves property-dependent native skin assigned after creation");
        check(!matched->property("nativeActionSkin").isValid(),"native business properties are not copied to owned action");
        native->setAttribute(Qt::WA_UnderMouse,false);matched->setAttribute(Qt::WA_UnderMouse,false);
        native->setStyleSheet(QStringLiteral("QRadioButton#btnUninstallGame { color: white; spacing: 7px; } QRadioButton#btnUninstallGame::indicator { width: 18px; height: 18px; background: #777777; } QRadioButton#btnUninstallGame:hover { color: #ffef00; }"));
        check(same(),"live source renderer follows subsequent native skin replacement without stale QSS");
        // Actual launcher ancestors contain bare declarations as well as full
        // selector blocks. Both are individually valid, not valid concatenated.
        radioParent->setStyleSheet(QStringLiteral("background-color:transparent;\nborder:none;"));
        native->setStyleSheet(QStringLiteral("QRadioButton { color: white; spacing: 7px; } QRadioButton::indicator { width: 16px; height: 16px; background: #777777; border: 2px solid white; }"));
        check(same(),"observed native bare ancestor declarations and button selectors retain identical rendering");
        {
            OptionsRadioButton legacy(native->text(),radioParent);legacy.setObjectName(QStringLiteral("zmlUninstallButton"));legacy.setGeometry(matched->geometry());legacy.setFont(native->font());legacy.setPalette(native->palette());legacy.setStyleSheet(buttonStyle(native));
            auto a=render(native),b=render(&legacy);
            check(a.bytesPerLine()==b.bytesPerLine() && std::memcmp(a.constBits(),b.constBits(),a.bytesPerLine()*native->size().height())!=0,"old ancestor-QSS concatenation reproduces default-renderer mismatch in owned fixture");
        }
        auto count=testUninstallRequests;matched->click();check(testUninstallRequests==count+1,"native radio action invokes only owned uninstall callback");
        delete radioParent;
        auto* ambiguous=new TestWidget(&top);
        for(int i=0;i<2;++i){auto* b=new OptionsButton(QString(),ambiguous);b->setObjectName(QStringLiteral("btnUninstallGame"));}
        scan();check(!ambiguous->findChild<QPushButton*>(QStringLiteral("zmlUninstallButton")),"ambiguous native target rejected");delete ambiguous;
        top.setObjectName(QStringLiteral("other_game_options"));auto* other=new OptionsButton(QString(),&top);other->setObjectName(QStringLiteral("btnUninstallGame"));
        scan();check(!top.findChild<QPushButton*>(QStringLiteral("zmlUninstallButton")),"unknown dialog preserved");
        std::cout<<"RESULT 29 native Qt checks; no desktop windows shown\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
